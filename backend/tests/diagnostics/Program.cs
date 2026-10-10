using ADProductScraper.Api;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using AngleSharp.Html.Parser;
if (args.Contains("--ljm-inspect"))
{
 using var client = new SafeWeb();
 var uri = new Uri("https://www.leatherjacketmaster.com/collections/womens-leather-jackets");
 var response = await client.Fetch(uri, CancellationToken.None);
 File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "ljm-collection.html"), response.Html);
 var doc = new HtmlParser().ParseDocument(response.Html);
 foreach (var node in doc.QuerySelectorAll("[class*='price'], select[name*='country'], select[name*='currency'], form[action*='localization']").Take(20))
  Console.WriteLine(node.OuterHtml[..Math.Min(2400, node.OuterHtml.Length)]);
 return;
}
if (args.Contains("--inspect-prices"))
{
 using var client = new SafeWeb();
 var uri = new Uri("https://decrum.com/collections/womens-leather-jackets");
 var response = await client.Fetch(uri, CancellationToken.None);
 var parsed = Extractor.Parse(response.Html, uri);
 Console.WriteLine($"Collection: HTTP {response.Status}, products={parsed.Products.Count}, both prices={parsed.Products.Count(p => p.OldPrice.Length > 0)}");
 foreach (var product in parsed.Products.Take(3))
 {
  var detail = await client.Fetch(new Uri(product.Url), CancellationToken.None);
  var path = Path.Combine(AppContext.BaseDirectory, new Uri(product.Url).Segments.Last() + ".html");
  File.WriteAllText(path, detail.Html);
  var doc = new HtmlParser().ParseDocument(detail.Html);
  Console.WriteLine($"{product.Url}: HTTP {detail.Status}; {path}");
  foreach (var node in doc.QuerySelectorAll(".t4s-product-price, .t4s-product__price, [data-product-price], del, .dc-was, [style*='line-through']").Take(6))
   Console.WriteLine(node.OuterHtml[..Math.Min(1800, node.OuterHtml.Length)]);
  var result = Extractor.Parse(detail.Html, new Uri(product.Url), detail: true).Products.FirstOrDefault();
  Console.WriteLine($"Extracted: {result?.Price} / {result?.OldPrice}");
 }
 return;
}
if (args.Contains("--ebay-http") || args.Contains("--ebay-browser"))
{
 using var client = new SafeWeb();
 await using var browser = args.Contains("--ebay-browser") ? await BrowserCollector.Start(CancellationToken.None) : null;
 var uri = new Uri("https://www.ebay.com/sch/i.html?_nkw=leather+jackets&_sacat=0&_from=R40");
 var seen = new HashSet<string>();
 for (var number = 1; number <= 3; number++)
 {
  var response = browser == null ? await client.Fetch(uri, CancellationToken.None) : (Status: 200, Html: await browser.Read(uri, true, Console.WriteLine, CancellationToken.None));
  File.WriteAllText(Path.Combine(AppContext.BaseDirectory, $"ebay-page-{number}.html"), response.Html);
  var page = Extractor.Parse(response.Html, uri);
  var added = page.Products.Count(p => seen.Add(Extractor.Key(p.Url)));
  Console.WriteLine($"eBay page {number}: HTTP {response.Status}, products={page.Products.Count}, new={added}, next={page.Next}");
  if (response.Status != 200 || added == 0) { Environment.ExitCode = 1; return; }
  var next = Pagination.Next(uri, page, true);
  if (next == null) { Environment.ExitCode = 1; return; }
  uri = new Uri(next);
 }
 return;
}
if(args.Contains("--browser")) {
 try {
 await using var browser=await BrowserCollector.Start(CancellationToken.None);
 var uri=new Uri("https://decrum.com/collections/mens-leather-jackets");
 var html=await browser.Read(uri,true,Console.WriteLine,CancellationToken.None);
 File.WriteAllText("backend/tests/diagnostics/mens-scrolled.html",html);
 var p=Extractor.Parse(html,uri);
 Console.WriteLine($"BROWSER: products={p.Products.Count}, links={p.DetailLinks.Count}, expected={p.ExpectedProducts}, next={p.Next}");
 }catch(Exception e){Console.WriteLine(e.ToString());Environment.ExitCode=1;}
 return;
}
var root=Path.GetFullPath("backend/tests/diagnostics/live-jobs");
Directory.CreateDirectory(root);
using var web=new SafeWeb();
using var logging=LoggerFactory.Create(b=>b.AddConsole());
using var jobs=new Jobs(new TestEnvironment(root),web,logging.CreateLogger<Jobs>());
var women=args.Contains("--women");
var ljm=args.Contains("--ljm-prices");
var id=jobs.Create(new StartRequest(ljm ? "https://www.leatherjacketmaster.com/collections/womens-leather-jackets" : "https://decrum.com/collections/"+(women?"womens":"mens")+"-leather-jackets", BrowserMode:true,RespectRobots:false, Currency:ljm ? "PKR" : ""));
await jobs.StartAsync(CancellationToken.None);
try {
 using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(8));
 var last="";
 while(jobs.Status(id) is "Queued" or "Running" or "WaitingForBrowser") {
 var summary=System.Text.Json.JsonSerializer.Serialize(jobs.Get(id));
 if(summary!=last){Console.WriteLine(summary);last=summary;}
 await Task.Delay(3000,timeout.Token);
 }
 Console.WriteLine("FINAL "+System.Text.Json.JsonSerializer.Serialize(jobs.Get(id)));
 var products=jobs.Products(id)!;
 Console.WriteLine($"REAL_PRODUCTS={products.Count(p=>new Uri(p.Url).AbsolutePath.Contains("/products/"))}, total={products.Length}");
 Console.WriteLine($"BOTH_PRICES={products.Count(p => p.Price.Length > 0 && p.OldPrice.Length > 0)}, MISSING_ORIGINAL={products.Count(p => p.OldPrice.Length == 0)}");
 File.WriteAllBytes(ljm ? "backend/tests/diagnostics/ljm-pkr-verified.xlsx" : "backend/tests/diagnostics/decrum-"+(women?"womens":"mens")+"-verified.xlsx",ExcelExport.Build(products,jobs.Status(id)));
 if(jobs.Status(id)!="Completed"||products.Length!=(ljm ? 68 : women?134:119))Environment.ExitCode=1;
 if(ljm && products.Any(p => p.Currency != "PKR")) Environment.ExitCode=1;
 if(args.Contains("--prices") && products.Any(p => p.OldPrice.Length == 0 || Pricing.ToNumber(p.OldPrice) <= Pricing.ToNumber(p.Price))) Environment.ExitCode=1;
}finally{await jobs.StopAsync(CancellationToken.None);}
sealed class TestEnvironment(string root):IWebHostEnvironment {
 public string ApplicationName{get;set;}="Diagnostic";
 public string EnvironmentName{get;set;}="Development";
 public string ContentRootPath{get;set;}=root;
 public string WebRootPath{get;set;}=root;
 public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();
 public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();
}

