using ClosedXML.Excel;

namespace ADProductScraper.Api;

public static class ExcelExport
{
    public static byte[] Build(IEnumerable<Product> rows, string status)
    {
        var products = rows.ToArray();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet("Products");
        var imageCount = Math.Clamp(products.Select(p => p.Images.Length).DefaultIfEmpty(1).Max(), 1, 30);
        var headers = new[] { "Product title", "Material", "Store name", "Price", "Original price", "Currency", "Product link", "Source page" }.Concat(Enumerable.Range(1, imageCount).Select(i => $"Image {i} URL")).ToArray();
        for (var c = 0; c < headers.Length; c++) sheet.Cell(1, c + 1).Value = headers[c];
        for (var r = 0; r < products.Length; r++)
        {
            var p = products[r];
            string NA(string v, string none = "Not available") => string.IsNullOrWhiteSpace(v) ? none : v;
            var fields = new[] { NA(p.Title), NA(p.Material), NA(p.Store), NA(p.Price), NA(p.OldPrice, p.Price.Length > 0 ? "No discount" : "Not available"), NA(p.Currency), p.Url, p.SourcePage }
                .Concat(Enumerable.Range(0, imageCount).Select(i => i < p.Images.Length ? p.Images[i] : i == 0 ? "Not available" : "")).ToArray();
            for (var c = 0; c < fields.Length; c++)
                {
                var cell = sheet.Cell(r + 2, c + 1);
                if ((c == 3 || c == 4) && Pricing.ToNumber(fields[c]) is { } n && !fields[c].Contains('–')) { cell.Value = n; cell.Style.NumberFormat.Format = "#,##0.##"; }
                else cell.Value = fields[c].Length > 32767 ? fields[c][..32767] : fields[c];
            }
        }
        sheet.Range(1, 1, Math.Max(products.Length + 1, 2), headers.Length).CreateTable("ProductData").Theme = XLTableTheme.TableStyleMedium2;
        sheet.SheetView.FreezeRows(1); sheet.Columns().Width = 24; sheet.Column(1).Width = 45; sheet.Column(7).Width = 55;
        sheet.Rows().Height = 23; sheet.Row(1).Height = 30;
        var images = workbook.AddWorksheet("All image links");
        images.Cell(1, 1).Value = "Product link"; images.Cell(1, 2).Value = "Image URL";
        var row = 2;
        foreach (var product in products) foreach (var image in product.Images)
        {
            if (row > 1048576) throw new InvalidOperationException("Too many image links for one Excel sheet. Use a smaller page limit.");
            images.Cell(row, 1).Value = product.Url; images.Cell(row++, 2).Value = image;
        }
        images.Columns().Width = 70; images.SheetView.FreezeRows(1);
        var info = workbook.AddWorksheet("Read me");
        string[][] notes = [["AD Product Scraper", "Product collection"], ["Exported (UTC)", DateTimeOffset.UtcNow.ToString("u")], ["Job status", status], ["Products", products.Length.ToString()], ["Missing values", "Blank means not published or not extracted. Values are not guessed."], ["Prices", "Price is the current (discounted) price; Original price is the struck-through price when shown. Currency is the code shown on the website."], ["Images", "URLs only; first 30 appear on Products. Every URL is listed in All image links."], ["Coverage", "Only discoverable HTML pages; access restrictions and page limits may make this a partial export."]];
        for (var r = 0; r < notes.Length; r++) { info.Cell(r + 1, 1).Value = notes[r][0]; info.Cell(r + 1, 2).Value = notes[r][1]; }
        info.Column(1).Width = 24; info.Column(2).Width = 110; info.Column(2).Style.Alignment.WrapText = true; info.Rows().Height = 32;
        using var stream = new MemoryStream(); workbook.SaveAs(stream); return stream.ToArray();
    }
}
