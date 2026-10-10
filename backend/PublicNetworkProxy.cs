using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ADProductScraper.Api;

// Local browser transport: checks DNS and connects to the checked public address.
// HTTPS is tunnelled unchanged, leaving certificate verification to the browser.
public sealed class PublicNetworkProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stop = new();
    private readonly ConcurrentDictionary<TcpClient, Task> connections = new();
    private readonly Task loop;
    public string Address { get; }
    public PublicNetworkProxy()
    {
        listener.Start();
        Address = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        loop = Accept();
    }
    private async Task Accept()
    {
        try
        {
            while (!stop.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stop.Token);
                var task = Serve(client);
                connections[client] = task;
                _ = task.ContinueWith(_ => { connections.TryRemove(client, out var unused); client.Dispose(); }, TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException) { }
        catch (SocketException) when (stop.IsCancellationRequested) { }
    }
    private async Task Serve(TcpClient client)
    {
        using (client)
        using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop.Token))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var ct = deadline.Token;
            var incoming = client.GetStream();
            var established = false;
            try
            {
                var header = new List<byte>(); var one = new byte[1];
                while (header.Count < 16384)
                {
                    if (await incoming.ReadAsync(one, ct) == 0) return;
                    header.Add(one[0]);
                    if (header.Count >= 4 && header[^4] == 13 && header[^3] == 10 && header[^2] == 13 && header[^1] == 10) break;
                }
                var lines = Encoding.ASCII.GetString(header.ToArray()).Split("\r\n");
                var request = lines[0].Split(' ');
                if (request.Length != 3 || header.Count >= 16384) throw new IOException("Invalid proxy request.");
                var tunnel = request[0] == "CONNECT";
                var target = SafeWeb.Validate(tunnel ? "https://" + request[1] : request[1]);
                if (!tunnel && target.Scheme != "http") throw new IOException("HTTPS requires CONNECT.");
                var addresses = await Dns.GetHostAddressesAsync(target.Host, ct);
                if (addresses.Length == 0 || addresses.Any(a => !SafeWeb.IsPublic(a))) throw new IOException("Private network target rejected.");
                using var remote = new TcpClient();
                await remote.ConnectAsync(addresses, target.Port, ct);
                var outgoing = remote.GetStream();
                if (tunnel) await incoming.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 Connection Established\r\n\r\n"), ct);
                else
                {
                    var forwarded = new StringBuilder($"{request[0]} {target.PathAndQuery} HTTP/1.1\r\nHost: {target.Authority}\r\nConnection: close\r\n");
                    foreach (var line in lines.Skip(1).Where(l => l.Length > 0 && !l.StartsWith("Host:", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Connection:", StringComparison.OrdinalIgnoreCase) && !l.StartsWith("Proxy-", StringComparison.OrdinalIgnoreCase))) forwarded.Append(line).Append("\r\n");
                    forwarded.Append("\r\n");
                    await outgoing.WriteAsync(Encoding.ASCII.GetBytes(forwarded.ToString()), ct);
                }
                established = true;
                deadline.CancelAfter(Timeout.InfiniteTimeSpan);
                var upload = incoming.CopyToAsync(outgoing, ct);
                var download = outgoing.CopyToAsync(incoming, ct);
                await Task.WhenAny(upload, download);
                deadline.Cancel();
                try { await Task.WhenAll(upload, download); } catch (Exception e) when (e is IOException or OperationCanceledException) { }
            }
            catch (Exception e) when (e is IOException or SocketException or ArgumentException or OperationCanceledException)
            {
                if (!established) try { await incoming.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"), stop.Token); } catch { }
            }
        }
    }
    public async ValueTask DisposeAsync()
    {
        stop.Cancel(); listener.Stop();
        await loop;
        var pending = connections.ToArray();
        foreach (var connection in pending) connection.Key.Dispose();
        try { await Task.WhenAll(pending.Select(x => x.Value)); } catch { }
        stop.Dispose();
    }
}
