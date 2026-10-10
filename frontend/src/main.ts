import {Component, signal, computed, OnDestroy, ErrorHandler, inject} from '@angular/core';
import {bootstrapApplication} from '@angular/platform-browser';
import {FormsModule} from '@angular/forms';
import {AppErrors, api, request, errorMessage} from './errors';

interface Product {title:string;url:string;images:string[];material:string;store:string;price:string;currency:string;sourcePage:string;oldPrice?:string}
interface Job {id:string;status:string;message:string;created:string;count:number;pages:number;warnings:string[];request:{url:string;demo:boolean;currency?:string;source?:string;keywords?:string;etsyShopId?:number}}
@Component({
  selector:'app-root', standalone:true, imports:[FormsModule],
  template:`
  <div class="app-shell">
    <aside class="sidebar">
      <a class="brand" href="/" aria-label="AD Product Scraper home"><span class="brand-mark"><img src="/ad-logo.png" alt="" width="28" height="28"></span><span class="brand-copy">AD Product Scraper</span></a>
      
      <p class="nav-label">COLLECTIONS</p>
      <button class="nav-item" [class.active]="view()==='collect'" [attr.aria-current]="view()==='collect'?'page':null" (click)="view.set('collect')"><span>▦</span> Product collector <span class="nav-arrow"><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M6 18 18 6M6 6h12v12"/></svg></span></button>
      <button class="nav-item" [class.active]="view()==='history'" [attr.aria-current]="view()==='history'?'page':null" (click)="view.set('history');refresh()"><span>◷</span> Collection history <span class="count-pill">{{jobs().length}}</span></button>
      
    </aside>
    <main>
      <header class="topbar"><span><span class="brand-crumb">AD Product Scraper</span> <span class="slash">/</span> <b>{{view()==='history'?'Collection history':'Product collector'}}</b></span><span class="top-status"><i></i> Excel exports only</span></header>
      <div class="content">
        <div class="page-heading"><div><h1>{{view()==='history'?'Collection history':'Product collector'}}</h1></div><button class="button secondary demo-button" [disabled]="busy()" (click)="start(true)"><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="m8 4 12 8-12 8V4Z"/></svg> &nbsp; Try a demo</button></div>
        @if(error()){<div class="alert error" role="alert">{{error()}} <button (click)="error.set('')" aria-label="Dismiss error">×</button></div>}
        @if(view()==='collect'){
          <section class="collector panel">
            <div class="section-top"><div class="step-icon">01</div><div><h2>Start a collection</h2><p>Use a website URL or search Etsy through its official API.</p></div><label class="page-limit" style="margin-left:auto">Pages to collect <select name="limit" [(ngModel)]="limit"><option [ngValue]="0">All available pages</option><option [ngValue]="2">First 2 pages (quick test)</option><option [ngValue]="5">First 5 pages</option><option [ngValue]="10">First 10 pages</option><option [ngValue]="50">First 50 pages</option><option [ngValue]="100">First 100 pages</option></select></label></div>
            <form (ngSubmit)="start(false)">
              <label class="field-label" for="source">Collection source</label>
              <select id="source" name="source" [(ngModel)]="source"><option value="website">Website URL</option><option value="etsy">Etsy API</option></select>
              @if(source==='etsy'){
                <p>Search public Etsy listings through the official API. App approval is required.</p>
                @if(etsyConfigured()===false){<p role="status">Etsy credentials are not configured. See the backend Etsy setup guide.</p>}
                <label class="field-label" for="keywords">Search keywords</label>
                <input id="keywords" name="keywords" [(ngModel)]="keywords" maxlength="200" placeholder="leather jackets" [required]="!etsyShopId">
                <label class="field-label" for="etsyShopId">Numeric shop ID (optional)</label>
                <input id="etsyShopId" name="etsyShopId" type="number" min="1" step="1" [(ngModel)]="etsyShopId" placeholder="Leave blank to search the marketplace">
                <p>Each page contains up to 100 listings. Prices use the currency provided by Etsy.</p>
                <button class="button primary" type="submit" [disabled]="busy() || etsyConfigured()===false">{{busy()?'Starting…':'Collect Etsy products'}}</button>
              }@else{
              <label class="field-label" for="url">Search results or product URL</label>
              <div class="url-row"><div class="url-input"><span><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M6 18 18 6M6 6h12v12"/></svg></span><input id="url" type="url" name="url" [(ngModel)]="url" (ngModelChange)="loadCurrency()" placeholder="https://www.etsy.com/search?q=ceramic+vase" required autocomplete="url"></div><button class="button primary" type="submit" [disabled]="busy()">{{busy()?'Starting…':'Collect products'}} <span><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M4 12h16m-7-7 7 7-7 7"/></svg></span></button></div>
              <div style="display:flex;gap:12px;align-items:center;margin-top:14px;flex-wrap:wrap"><label class="page-limit">Website currency <select name="currency" [(ngModel)]="currency" (ngModelChange)="rememberCurrency()"><option value="">Website default / URL</option>@for(code of currencies;track code){<option [value]="code">{{code}}</option>}</select></label><small>Match the currency selected on the store. Your choice is remembered for this website.</small></div>
              }
            </form>
          </section>
          
        }
        @if(view()==='history'){
          <section class="panel history"><div class="section-top"><h2>Saved collections</h2><span class="tag">{{jobs().length}} COLLECTIONS</span></div>
          @for(j of jobs();track j.id){<button class="history-row" (click)="select(j.id)"><span><b>{{j.request.demo?'Demo · Home essentials':j.request.source==='etsy'?'Etsy API · '+(j.request.keywords||'Shop '+j.request.etsyShopId):host(j.request.url)}}</b><small>{{date(j.created)}}</small></span><span>{{j.count}} products</span><span class="status-badge" [class.failed]="j.status==='Failed'">{{j.status}}</span><span><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M4 12h16m-7-7 7 7-7 7"/></svg></span></button>}@empty{<p class="history-empty">No collections yet. Paste a search link or try the demo to begin.</p>}</section>
        }
        @if(current();as job){
          <section class="panel results">
            <div class="results-heading"><div><div class="eyebrow">{{job.request.demo?'DEMO DATA · NOT LIVE MARKETPLACE RESULTS':'YOUR COLLECTION'}}</div><h2>{{job.request.demo?'A little collection of home comforts':host(job.request.url)}}</h2></div><div class="result-actions">@if(active()){<button class="button secondary small" (click)="action('cancel')" [disabled]="job.status==='Stopping'">Stop</button>}@if(['Failed','Cancelled','Paused'].includes(job.status)){<button class="button secondary small" (click)="action('resume')">Resume</button>}<button class="button primary small" [disabled]="job.count===0 || exporting()" (click)="download()"><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M12 3v13m-6-6 6 6 6-6M5 17v4h14v-4"/></svg> &nbsp; {{exporting()?'Preparing…':'Download Excel'}}</button></div></div>
            <div class="stats"><div><span>PRODUCTS COLLECTED</span><strong>{{job.count}}<small>unique products</small></strong></div><div><span>PAGES COMPLETED</span><strong>{{job.pages}}<small>automatically followed</small></strong></div><div><span>COLLECTION STATUS</span><strong class="status-text" [class.failed-text]="job.status==='Failed'">{{job.status}}<small>{{job.request.demo?'Sample collection':'Live collection'}}</small></strong></div></div>
            <div class="progress-line" [class.running]="active()"></div><p class="job-message" aria-live="polite">{{job.message}}</p>
            @if(job.warnings.length){<details class="warnings"><summary>{{job.warnings.length}} collection note{{job.warnings.length===1?'':'s'}}</summary>@for(w of job.warnings;track w){<p>{{w}}</p>}</details>}
            <div class="table-wrap"><table><thead><tr><th>PRODUCT</th><th>MATERIAL</th><th>STORE</th><th>PRICE</th><th>IMAGES</th><th></th></tr></thead><tbody>@for(p of products();track p.url){<tr><td><div class="product-cell"><span class="product-thumb">▧</span><span class="product-name">{{p.title}}<small>{{host(p.url)}}</small></span></div></td><td>@if(p.material){<span>{{p.material}}</span>}@else{<span class="na">Not available</span>}</td><td>@if(p.store){<span>{{p.store}}</span>}@else{<span class="na">Not available</span>}</td><td class="price">@if(p.price){<b class="price-now">{{p.currency}} {{p.price}}</b>@if(p.oldPrice){<s class="price-old">{{p.currency}} {{p.oldPrice}}</s>}}@else{<span class="na">Not available</span>}</td><td><div class="product-image-links">@if(!p.images.length){<span class="na">Not available</span>}@for(image of p.images;track $index){<a class="product-image-link" [href]="image" target="_blank" rel="noopener noreferrer" [title]="image" [attr.aria-label]="'Open image ' + ($index + 1) + ' for ' + p.title + ' in a new tab'">Image {{$index + 1}}<svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M6 18 18 6M6 6h12v12"/></svg></a>}@empty{<span class="no-images">No images</span>}</div></td><td><a class="external" [href]="p.url" target="_blank" rel="noopener noreferrer" aria-label="Open product"><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M6 18 18 6M6 6h12v12"/></svg></a></td></tr>}@empty{<tr><td colspan="6" class="table-empty">{{active()?'Collecting the first products…':'No products collected. See the collection message above.'}}</td></tr>}</tbody></table></div>
            <div class="table-footer"><span>Showing {{products().length?offset()+1:0}}–{{offset()+products().length}} of {{job.count}} products</span><div><button aria-label="Previous products" [disabled]="offset()===0" (click)="page(-1)"><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M20 12H4m7-7-7 7 7 7"/></svg></button><button aria-label="Next products" [disabled]="offset()+50>=job.count" (click)="page(1)"><svg class="ui-icon" aria-hidden="true" viewBox="0 0 24 24"><path d="M4 12h16m-7-7 7 7-7 7"/></svg></button></div></div>
          </section>
        }@else if(view()==='collect'){
          <section class="panel welcome"><div class="empty-art"><div class="sheet-icon"><span></span><i></i><i></i><i></i><i></i></div><span class="floating-badge">XLSX</span></div><p class="welcome-line">Paste a search link above to start.</p></section>
        }
        <footer><span></span><span>AD Product Scraper <span class="footer-dot">●</span> v1.0</span></footer>
      </div>
    </main>
  </div>`
})
class App implements OnDestroy {
  source='website'; keywords=''; etsyShopId:number|null=null; etsyConfigured=signal<boolean|null>(null);
  url=''; limit=0; currency=''; currencies=['PKR','USD','GBP','EUR','AUD','CAD','AED','INR','SAR','NZD','CHF','JPY','SGD'];
  loadCurrency(){try{const u=new URL(this.url);this.currency=(u.searchParams.get('currency')??u.searchParams.get('currency_code')??localStorage.getItem('catalog.currency.'+u.host)??this.jobs().find(j=>{try{return new URL(j.request.url).host===u.host && !!j.request.currency;}catch{return false;}})?.request.currency??'').toUpperCase();}catch{}}
  rememberCurrency(){try{localStorage.setItem('catalog.currency.'+new URL(this.url).host,this.currency);}catch{}}
  private errors=inject(AppErrors);
  jobs=signal<Job[]>([]); current=signal<Job|null>(null); products=signal<Product[]>([]); error=this.errors.message; busy=signal(false); exporting=signal(false); view=signal('collect'); offset=signal(0);
  active=computed(()=>['Queued','Running','Stopping','WaitingForBrowser'].includes(this.current()?.status??''));
  private polling=false;
  private timer=setInterval(()=>void this.poll(),1500);
  private onUnhandled=(event:PromiseRejectionEvent)=>{this.errors.handleError(event.reason);event.preventDefault();};
  private onError=(event:ErrorEvent)=>{this.errors.handleError(event.error??event.message);};
  constructor(){window.addEventListener('unhandledrejection',this.onUnhandled);window.addEventListener('error',this.onError);void this.refresh();void this.loadEtsyStatus();}
  ngOnDestroy(){clearInterval(this.timer);window.removeEventListener('unhandledrejection',this.onUnhandled);window.removeEventListener('error',this.onError);}
  api=api;
  async loadEtsyStatus(){try{this.etsyConfigured.set((await this.api<{configured:boolean}>('/etsy/status')).configured);}catch{this.etsyConfigured.set(null);}}
  async refresh(){try{this.jobs.set(await this.api<Job[]>('/jobs'));}catch(e){this.error.set(this.message(e));}}
  async start(demo:boolean){if(this.busy())return;this.busy.set(true);this.error.set('');try{const result=await this.api<{id:string}>('/jobs',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({url:this.url,maxPages:this.limit,visitDetails:false,demo,source:demo?'website':this.source,keywords:this.keywords,etsyShopId:this.source==='etsy'&&!demo?this.etsyShopId:null,browserMode:!demo&&this.source!=='etsy',respectRobots:false,currency:demo||this.source==='etsy'?'':this.currency})});this.view.set('collect');await this.select(result.id);await this.refresh();}catch(e){this.error.set(this.message(e));}finally{this.busy.set(false);}}
  private selection=0;
  async select(id:string){const selection=++this.selection;try{const [job,rows]=await Promise.all([this.api<Job>('/jobs/'+id),this.api<Product[]>('/jobs/'+id+'/products?take=50')]);if(selection===this.selection){this.offset.set(0);this.current.set(job);this.products.set(rows);}}catch(e){this.error.set(this.message(e));}}
  async poll(){if(this.polling||!this.active())return;this.polling=true;try{const id=this.current()!.id;const job=await this.api<Job>('/jobs/'+id);const rows=await this.api<Product[]>('/jobs/'+id+'/products?take=50&skip='+this.offset());if(this.current()?.id===id){this.current.set(job);this.products.set(rows);}if(!this.active())await this.refresh();}catch(e){this.error.set(this.message(e));}finally{this.polling=false;}}
  async action(action:string){if(!this.current())return;try{const id=this.current()!.id;await this.api('/jobs/'+id+'/'+action,{method:'POST'});this.current.set(await this.api<Job>('/jobs/'+id));}catch(e){this.error.set(this.message(e));}}
  private paging=false;
  async page(direction:number){const job=this.current();if(!job||this.paging)return;this.paging=true;const offset=Math.max(0,this.offset()+direction*50);try{const rows=await this.api<Product[]>('/jobs/'+job.id+'/products?take=50&skip='+offset);if(this.current()?.id===job.id){this.offset.set(offset);this.products.set(rows);}}catch(e){this.error.set(this.message(e));}finally{this.paging=false;}}
  async download(){const job=this.current();if(!job||this.exporting())return;this.exporting.set(true);let url:string|undefined;try{const response=await request('/jobs/'+job.id+'/excel',undefined,120000);const blob=await response.blob();if(!blob.size)throw new Error('The Excel download was empty. Please retry.');url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download='products-'+job.id.slice(0,8)+'.xlsx';a.click();}catch(e){this.error.set(this.message(e));}finally{if(url){const downloadUrl=url;setTimeout(()=>URL.revokeObjectURL(downloadUrl),10000);}this.exporting.set(false);}}
  isEtsy(){try{const u=new URL(this.url);return u.protocol==='https:' && (u.hostname==='etsy.com' || u.hostname.endsWith('.etsy.com'));}catch{return false;}}
  host(url:string){try{return new URL(url).hostname.replace(/^www\./,'');}catch{return 'Website';}}
  date(value:string){return new Date(value).toLocaleString();}
  message(e:unknown){return errorMessage(e);}
}
bootstrapApplication(App,{providers:[{provide:ErrorHandler,useExisting:AppErrors}]}).catch(error=>{console.error('Application startup failed',error);const root=document.querySelector('app-root');if(root)root.textContent='The application could not start. Refresh the page and check that the backend is running.';});



