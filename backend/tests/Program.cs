using ADProductScraper.Api;
using ClosedXML.Excel;
using System.Net;

if (args.Contains("--etsy-only")) { await EtsyChecks.Run(); return; }
if (args.Contains("--errors-only")) { await ErrorHandlingChecks.Run(); return; }
var passed = 0;
void Check(bool condition, string name) { if (!condition) throw new Exception("FAIL: " + name); Console.WriteLine("PASS: " + name); passed++; }
var source = new Uri("https://store.example/search?page=1");
var parsed = Extractor.Parse("""
<article class="product-card"><a href="/products/cup"><h2>Ceramic &amp; clay cup</h2><img src="/cup.jpg"></a><span class="price">£12.99</span><span class="shop-name">Cup Studio</span></article><a rel="next" href="?page=2">Next</a>
""", source);
Check(parsed.Products.Single().Title == "Ceramic & clay cup", "HTML entities and product title");
Check(parsed.Products.Single().Images.Single() == "https://store.example/cup.jpg", "Relative image URLs");
Check(parsed.Next == "https://store.example/search?page=2", "Pagination resolves relative URLs");
var collection = new Uri("https://decrum.com/collections/womens-leather-jackets?sort_by=best-selling");
ParsedPage Batch(int start, int count) => Extractor.Parse(string.Concat(Enumerable.Range(start, count).Select(i =>
    $"<article class='product-card'><a href='/products/jacket-{i}'><h2>Leather jacket {i}</h2></a><span class='price'>$99</span></article>")), collection);
var firstBatch = Batch(1, 21);
var secondBatch = Batch(22, 21);
var nextBatchUrl = Pagination.Next(collection, firstBatch, true);
Check(firstBatch.Products.Count == 21 && nextBatchUrl == collection.AbsoluteUri + "&page=2", "Infinite scroll: 21 products continue without a Next button");
Check(Pagination.Next(new Uri(nextBatchUrl!), secondBatch, true) == collection.AbsoluteUri + "&page=3", "Infinite scroll: subsequent batches preserve filters");
Check(Pagination.Next(new Uri(nextBatchUrl!), secondBatch, false) == null, "Infinite scroll: repeated products stop pagination");
Check(Pagination.Next(collection, Batch(43, 1), true) != null, "Infinite scroll: single-product final batch is checked for more");
Check(Pagination.Next(collection, Batch(1, 0), false) == null, "Infinite scroll: empty batch stops pagination");
Check(Pagination.Next(source, parsed, false) == parsed.Next, "Explicit Next links retain precedence");
var linksOnly = Extractor.Parse("""<script type="application/ld+json">{"@type":"ItemList","itemListElement":[{"@type":"ListItem","position":1,"url":"https://decrum.com/products/jacket-1"}]}</script>""", collection);
Check(linksOnly.Products.Count == 0 && linksOnly.DetailLinks.Count == 1 && Pagination.Next(collection, linksOnly, true) != null, "Links-only collection continues after detail products are added");
var singleDetail = Extractor.Parse("<h1>Wood tray</h1><span class='price'>$20</span>", source, detail:true);
Check(Pagination.Next(source, singleDetail, true) == null, "Product detail does not probe collection pages");
var t4s = Extractor.Parse("""
<link rel="next" href="?page=2">
<div class="t4s-main-collection-page"><div class="t4s-active-filters__count"><span class="cp">119</span> Products Found</div>
<div class="t4s-product"><a data-pr-href href="/products/leather-jacket"></a><h3 class="t4s-product-title">Real Leather Jacket</h3><div class="t4s-product-price" data-pr-price>$199.00 USD</div><img data-src="/jacket.webp"></div></div>
<script type="application/ld+json">{"@type":"BreadcrumbList","itemListElement":[{"@type":"ListItem","url":"https://decrum.com/collections/womens-leather-jackets"}]}</script>
<script type="application/ld+json">{"@type":"ItemList","itemListElement":[{"@type":"ListItem","url":"https://decrum.com/products/metadata-only"}]}</script>
""", collection);
Check(t4s.Products.Count == 1 && t4s.Products[0].Price == "199" && t4s.Products[0].Images.Length == 1, "Decrum: rendered T4S cards supply title, price and lazy image");
Check(t4s.DetailLinks.SequenceEqual(t4s.Products.Select(p => p.Url)), "Decrum: breadcrumbs and separately paginated metadata are excluded");
Check(t4s.Next!.EndsWith("?page=2") && t4s.IsCollection && t4s.ExpectedProducts == 119, "Decrum: head pagination and advertised total are detected");
var lastT4s = Extractor.Parse("<div class='t4s-main-collection-page'><a data-load-more data-is-prev href='?page=3'>Load previous</a><div class='t4s-active-filters__count'><span class='cp'>119</span></div></div><h1>Men's Leather Jackets</h1>", collection);
Check(lastT4s.Next == null && lastT4s.Products.Count == 0 && lastT4s.DetailLinks.Count == 0 && lastT4s.IsCollection, "Decrum: previous-page controls and collection heading are not products or Next links");
var scrollT4s = Extractor.Parse("<div class='t4s-main-collection-page'><a data-load-more data-load-onscroll href='?page=4'>Load more</a></div>", collection);
Check(scrollT4s.Next!.EndsWith("?page=4"), "Decrum: invisible scroll-trigger link is followed");
var dynamicPriceScript = "<script>function tagCard(pe) { pe.insertAdjacentHTML('beforeend', '<span class=\"dc-was\">$249.00</span>'); }</script>";
var dynamicCard = "<div class='t4s-main-collection-page'><div class='t4s-product'><a href='/products/jacket'><h3>Leather Jacket</h3></a><div class='t4s-product-price'>$189.00 USD</div></div></div>";
var unrenderedPrices = Extractor.Parse(dynamicCard + dynamicPriceScript, collection);
Check(unrenderedPrices.RequiresPriceRendering && unrenderedPrices.Products.Single().OldPrice == "", "Decrum detects JavaScript-only original price without guessing it");
var renderedPrices = Extractor.Parse(dynamicCard.Replace("$189.00 USD", "$189.00 <span class='dc-was'>$249.00</span>") + dynamicPriceScript, collection);
Check(!renderedPrices.RequiresPriceRendering && renderedPrices.Products.Single().Price == "189" && renderedPrices.Products.Single().OldPrice == "249", "Decrum rendered card preserves current and crossed-out prices");
Check(Extractor.Merge(renderedPrices.Products.Single(), unrenderedPrices.Products.Single()).OldPrice == "249", "Decrum original price survives product merging");
var ljmHtml = """
<form action='/localization'><input name='country_code' value='PK'><a data-value='US'>United States (USD $)</a><a data-value='PK'>Pakistan (PKR ₨)</a></form>
<div class='grid-product' data-product-id='123'><a href='/products/jacket'><div class='grid-product__title'>Women's Brown Biker Leather Jacket</div></a><div class='grid-product__price'><span class='visually-hidden'>Regular price</span><span class='grid-product__price--original'>Rs. 68,600.00</span><span class='visually-hidden'>Sale price</span>from Rs. 34,300.00<span class='grid-product__price--savings'>Save 50%</span></div></div>
<script type='application/ld+json'>{"@type":"Product","url":"https://store.example/products/jacket","name":"Cached USD title","offers":{"price":"245","priceCurrency":"USD"}}</script>
""";
var ljmPrices = Extractor.Parse(ljmHtml, new Uri("https://store.example/collections/jackets")).Products.Single();
Check(ljmPrices.Price == "34,300" && ljmPrices.OldPrice == "68,600", "Impulse theme extracts unwrapped sale price and separate original without the savings percentage");
Check(ljmPrices.Currency == "PKR" && ljmPrices.Title == "Women's Brown Biker Leather Jacket", "Selected market and rendered card override cached USD metadata");
Check(CurrencySelection.Resolve("", "https://store.example/?currency=pkr") == "PKR" && CurrencySelection.Resolve("GBP", "https://store.example/?currency=USD") == "GBP", "Currency URL inference and explicit selection precedence");
var badCurrency = false; try { CurrencySelection.Resolve("PKR;USD", source.AbsoluteUri); } catch (ArgumentException) { badCurrency = true; }
Check(badCurrency, "Invalid currency input is rejected");
var emptyImpulse = Extractor.Parse("<div id='CollectionAjaxContent'><h1>Women's Leather Jackets</h1></div><div class='price'>$0.00</div>", new Uri("https://store.example/collections/jackets?page=3"));
Check(emptyImpulse.IsCollection && emptyImpulse.Products.Count == 0 && emptyImpulse.DetailLinks.Count == 0, "Empty Impulse collection cannot become a zero-priced product from cart subtotal");
var ebaySource = new Uri("https://www.ebay.com/sch/i.html?_nkw=jacket&_pgn=2&_ipg=60");
Check(Pagination.Next(ebaySource, firstBatch, true) == "https://www.ebay.com/sch/i.html?_nkw=jacket&_pgn=3&_ipg=60", "eBay increments _pgn and preserves search filters");
Check(Pagination.Next(new Uri("https://www.ebay.co.uk/sch/i.html?_nkw=coat"), firstBatch, true)!.EndsWith("&_pgn=2"), "eBay first page uses native pagination parameter");
var staleNext = firstBatch with { Next = "https://www.ebay.com/sch/i.html?_nkw=jacket&_pgn=2" };
Check(Pagination.Next(ebaySource, staleNext, true)!.Contains("_pgn=3"), "eBay stale Next URL cannot send page two back to itself");
Check(Pagination.Next(ebaySource, firstBatch with { Next = "https://other.example/?page=3" }, true)!.Contains("_pgn=3"), "eBay ignores unrelated Next links");
var numberedEbay = Extractor.Parse("<nav class='pagination'><a href='?_nkw=jacket&_pgn=1'>1</a><span aria-current='page'>2</span><a href='?_nkw=jacket&_pgn=3'>3</a></nav>", ebaySource);
Check(numberedEbay.Next!.Contains("_pgn=3"), "eBay discovers numbered forward page without Next anchor");
var lastEbay = Extractor.Parse("<button class='pagination__next' aria-disabled='true'>Next page</button>", ebaySource);
Check(lastEbay.PaginationEnded && Pagination.Next(ebaySource, firstBatch with { PaginationEnded = true }, true) == null, "eBay disabled native Next confirms pagination end");
Check(!Extractor.Parse("<h1>leather jackets</h1>", ebaySource).PaginationEnded, "Missing eBay results do not prove pagination ended");
Check(SafeWeb.IsTemporaryError("<title>Error Page | eBay</title><p>Something went wrong on our end</p>") && SafeWeb.Classify(200, "<title>Error Page | eBay</title><p>Something went wrong on our end</p>") == PageKind.Blocked, "eBay HTTP 200 error page is not readable search results");
Check(Pagination.Next(new Uri("https://store.example/search?pg=4&q=coat"), firstBatch, true)!.Contains("pg=5&q=coat"), "Existing pagination parameter is preserved");
Check(Extractor.Parse("<a aria-label='NEXT PAGE' href='?page=3'>Continue</a>", source).Next!.EndsWith("page=3"), "Case insensitive next-page label");
var nestedFields = Extractor.Parse("<h1>Watch</h1><dl><div><dt><span>Material</span></dt><dd>Stainless steel</dd></div></dl><span data-seller-name='Watch Shop'></span>", source, detail:true).Products.Single();
Check(nestedFields.Material == "Stainless steel" && nestedFields.Store == "Watch Shop", "Nested material labels and seller data attributes");
if (args.Contains("--pagination-only")) { Console.WriteLine($"{passed} pagination checks passed."); return; }
Check(parsed.Products.Single().Material == "Ceramic", "Listing title material is extracted");
var detail = Extractor.Parse("""
<script type="application/ld+json">{"@graph":[{"@type":"Product","name":"Linen Bag","image":["/a.jpg",{"url":"/b.jpg"}],"material":["Linen","Cotton"],"offers":[{"price":"24.00","priceCurrency":"USD","seller":{"name":"Maker"}}]}]}</script><h1>Linen Bag</h1>
""", new Uri("https://store.example/products/bag"), detail:true).Products.Single();
Check(detail.Material == "Linen; Cotton" && detail.Images.Length == 2, "Structured product arrays and images");
Check(detail.Store == "Maker" && detail.Price == "24" && detail.Currency == "USD", "Offer price, currency and seller");
var table = Extractor.Parse("<h1>Wood tray</h1><table><tr><th>Material:</th><td>Oak</td></tr></table>", source, detail:true).Products.Single();
Check(table.Material == "Oak", "Material specification table");
var ebay = Extractor.Parse("<li class='s-item'><a class='s-item__link' href='/itm/12345'><h3 class='s-item__title'>Vintage watch</h3></a><span class='s-item__price'>$20.00</span></li><a class='pagination__next' href='?page=2'>Next</a>", new Uri("https://www.ebay.com/sch/test"));
Check(ebay.Products.Single().Price == "20" && ebay.Next != null, "eBay adapter fixture");
var amazon = Extractor.Parse("<div data-component-type='s-search-result' data-asin='B012345678'><a href='/dp/B012345678'><h2>Desk lamp</h2></a><span class='a-price'><span class='a-offscreen'>$29.95</span></span></div><a class='s-pagination-next' href='?page=2'>Next</a>", new Uri("https://www.amazon.com/s?k=lamp"));
Check(amazon.Products.Single().Title == "Desk lamp" && amazon.Products.Single().Price == "29.95", "Amazon adapter fixture");
var etsy = Extractor.Parse("<div data-listing-id='123'><a href='/listing/123/cup'><h3>Handmade cup</h3></a><span class='currency-value'>18.50</span></div>", new Uri("https://www.etsy.com/search?q=cup"));
Check(etsy.Products.Single().Price == "18.50", "Etsy adapter fixture");
var custom = Extractor.Parse("<section><a class='go' href='/item/1'><b class='name'>Custom</b></a></section><a id='more' href='?p=2'>More</a>", source, new Selectors(Card:"section", Title:".name", Link:".go", Next:"#more"));
Check(custom.Products.Single().Title == "Custom" && custom.Next!.EndsWith("?p=2"), "Custom selectors");
var fullTitle = Extractor.Parse("<div class='product-card'><a title='A complete product title' href='/p/1'>A complete…</a></div>", source, new Selectors(Title:"a"));
Check(fullTitle.Products.Single().Title == "A complete product title", "Full title attribute preferred over truncated display text");
Check(Extractor.Key("https://amazon.com/dp/B012345678?ref=a") == Extractor.Key("https://amazon.com/gp/product/B012345678?tag=b"), "Marketplace product deduplication");
Check(Extractor.Link(source, "javascript:alert(1)") == null, "Non-HTTP links rejected");
var robots = RobotsRules.Parse("User-agent: *\nDisallow: /private\nAllow: /private/public\nDisallow: /*?secret=*$");
Check(!robots.Allows(new Uri("https://example.com/private/a")) && robots.Allows(new Uri("https://example.com/private/public/a")), "Robots longest matching rule");
Check(!robots.Allows(new Uri("https://example.com/page?secret=1")), "Robots wildcard/end anchor");
var etsySearch = new Uri("https://www.etsy.com/search?q=leather%20jackets&ref=search_bar");
Check(RobotsRules.Parse("User-agent: *\nDisallow: /search?*q=").BlockingRule(etsySearch) == "/search?*q=", "Exact reported Etsy URL reproduces robots advisory");
var specific = RobotsRules.Parse("User-agent: *\nDisallow: /\nUser-agent: ADProductScraper\nAllow: /");
Check(specific.Allows(source), "Specific robots group takes precedence");
foreach (var ip in new[] { "127.0.0.1", "10.0.0.1", "169.254.169.254", "172.16.0.1", "192.168.1.1", "::1", "fc00::1", "::ffff:127.0.0.1" })
    Check(!SafeWeb.IsPublic(IPAddress.Parse(ip)), "SSRF private address rejected: " + ip);
Check(SafeWeb.IsPublic(IPAddress.Parse("8.8.8.8")), "Public address allowed");
var badUrl = false; try { SafeWeb.Validate("file:///etc/passwd"); } catch (ArgumentException) { badUrl = true; }
Check(badUrl, "Non-web URL rejected");
var formula = detail with { Title = "=HYPERLINK(\"https://bad.example\")", Material = "+SUM(1,2)" };
var xlsx = ExcelExport.Build([formula], "Completed");
using var workbook = new XLWorkbook(new MemoryStream(xlsx));
Check(workbook.Worksheet("Products").Cell(2,1).GetString() == formula.Title && !workbook.Worksheet("Products").Cell(2,1).HasFormula, "Excel formula injection protection");
Check(workbook.Worksheet("Products").Cell(2,9).GetString() == detail.Images[0], "Excel image link cells");
Check(workbook.Worksheet("All image links").LastRowUsed()!.RowNumber() == 3, "All image URLs retained");
Check(workbook.Worksheet("Products").Tables.Single().ShowAutoFilter, "Excel filters enabled");
await using (var proxy = new PublicNetworkProxy())
{
    foreach (var request in new[] { "CONNECT 127.0.0.1:443 HTTP/1.1\r\nHost: 127.0.0.1:443\r\n\r\n", "GET http://169.254.169.254/latest/meta-data HTTP/1.1\r\nHost: 169.254.169.254\r\n\r\n" })
    {
        using var tcp = new System.Net.Sockets.TcpClient();
        var endpoint = new Uri(proxy.Address);
        await tcp.ConnectAsync(endpoint.Host, endpoint.Port);
        await tcp.GetStream().WriteAsync(System.Text.Encoding.ASCII.GetBytes(request));
        var bytes = new byte[256];
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var read = await tcp.GetStream().ReadAsync(bytes, deadline.Token);
        Check(System.Text.Encoding.ASCII.GetString(bytes, 0, read).StartsWith("HTTP/1.1 403"), "Browser transport rejects private target: " + request.Split('\r')[0]);
    }
}
var booksFixture = Path.Combine(AppContext.BaseDirectory, "books-fixtures");
var books = Extractor.Parse(File.ReadAllText(Path.Combine(booksFixture, "page1.html")), new Uri("https://books.toscrape.com/"));
Check(books.Products.Count == 20, "Books live HTML: 20 listing products");
Check(books.Products[0].Title == "A Light in the Attic" && books.Products[0].Price == "51.77", "Books full title and price");
Check(books.Products.All(p => p.Images.Length > 0 && p.Images.All(i => i.StartsWith("https://books.toscrape.com/"))), "Books absolute image links");
Check(books.Next == "https://books.toscrape.com/catalogue/page-2.html", "Books next page link");
var books2 = Extractor.Parse(File.ReadAllText(Path.Combine(booksFixture, "page2.html")), new Uri(books.Next!));
Check(books.Products.Concat(books2.Products).Select(p => Extractor.Key(p.Url)).Distinct().Count() == 40, "Books two pages: 40 unique products");
var book = Extractor.Parse(File.ReadAllText(Path.Combine(booksFixture, "detail.html")), new Uri(books.Products[0].Url), detail: true).Products.Single();
Check(book.Title == "A Light in the Attic" && book.Price == "51.77" && book.Images.Length > 0, "Books detail page title, price and image");
using var booksExcel = new XLWorkbook(new MemoryStream(ExcelExport.Build(books.Products.Concat(books2.Products).ToList(), "Completed")));
Check(booksExcel.Worksheet("Products").LastRowUsed()!.RowNumber() == 41, "Books Excel: 40 product rows");
Product Fields(string html) => Extractor.Parse(html, new Uri("https://store.example/products/test"), detail: true).Products.Single();
Check(Fields("<h1>Organic cotton shirt</h1><p>Fabric composition: 80% cotton, 20% linen</p>").Material == "80% cotton, 20% linen", "Composition labels preserve percentages");
Check(Fields("<h1>Jacket</h1><section class='short-description'><p>Made from genuine leather with a polyester lining.</p></section>").Material == "genuine leather; polyester", "Materials from short description");
Check(Fields("<h1>Linen shirt</h1>").Material == "Linen", "Material from product title");
Check(Fields("<h1>Bag</h1><p>No leather. Made of nylon.</p>").Material == "nylon", "Negated materials excluded");
Check(Fields("<h1>Watch</h1><aside class='related-products'><p>Leather belt $20</p></aside>").Material == "", "Related product material excluded");
Check(Fields("<h1>Bag</h1><div class='price'><del>$90</del><ins>$60</ins></div>").Price == "60", "Sale price excludes crossed-out original");
Check(Fields("<h1>Bag</h1><meta property='product:price:amount' content='49.95'>").Price == "49.95", "Product price metadata");
Check(Fields("<h1>Bag</h1><div data-price='27.50'></div>").Price == "27.50", "Price data attribute");
Check(Fields("<h1>Bag</h1><p>PKR 2,499</p><footer>$5 delivery</footer>").Price == "2,499", "Unlabelled unique currency amount");
Check(Fields("<h1>Bag</h1><p>$20</p><p>$40</p>").Price == "", "Ambiguous prices remain blank");
Check(Fields("<h1>Bag</h1><div class='related-products'><span class='price'>$5</span></div><p>Price: $30</p>").Price == "30", "Related product price excluded");
Console.WriteLine($"\n{passed} checks passed.");

