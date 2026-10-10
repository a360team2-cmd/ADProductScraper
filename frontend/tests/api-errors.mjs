import {readFile} from 'node:fs/promises';
import assert from 'node:assert/strict';
import ts from 'typescript';

const source=await readFile(new URL('../src/api.ts',import.meta.url),'utf8');
const compiled=ts.transpileModule(source,{compilerOptions:{target:ts.ScriptTarget.ES2022,module:ts.ModuleKind.ES2022}}).outputText;
const {api,request}=await import('data:text/javascript;base64,'+Buffer.from(compiled).toString('base64'));
const original=globalThis.fetch;
let passed=0;
async function check(name,test){await test();console.log('PASS: '+name);passed++;}
try {
  await check('JSON success',async()=>{globalThis.fetch=async()=>new Response('{"id":"abc"}');assert.deepEqual(await api('/jobs'),{id:'abc'});});
  await check('Empty action response',async()=>{globalThis.fetch=async()=>new Response(null,{status:204});assert.equal(await api('/jobs/abc/cancel'),null);});
  await check('Structured server error includes reference',async()=>{globalThis.fetch=async()=>new Response('{"error":"Storage failed","traceId":"ref-1"}',{status:503});await assert.rejects(()=>api('/jobs'),/Storage failed.*ref-1/);});
  await check('Missing collection with empty response',async()=>{globalThis.fetch=async()=>new Response(null,{status:404});await assert.rejects(()=>api('/jobs/missing'),/Collection not found/);});
  await check('Non-JSON proxy failure',async()=>{globalThis.fetch=async()=>new Response('<html>Bad gateway</html>',{status:502});await assert.rejects(()=>api('/jobs'),/Request failed \(502\)/);});
  await check('Malformed successful response',async()=>{globalThis.fetch=async()=>new Response('<html>Wrong server</html>');await assert.rejects(()=>api('/jobs'),/backend returned an invalid response/);});
  await check('Offline connection',async()=>{globalThis.fetch=async()=>{throw new TypeError('Failed to fetch');};await assert.rejects(()=>api('/jobs'),/Make sure the backend is running/);});
  await check('Request timeout aborts pending fetch',async()=>{globalThis.fetch=(_url,{signal})=>new Promise((_resolve,reject)=>{signal.addEventListener('abort',()=>reject(signal.reason),{once:true});});const keepAlive=setTimeout(()=>{},100);try{await assert.rejects(()=>request('/jobs',undefined,5),/request timed out/);}finally{clearTimeout(keepAlive);}});
  console.log(`${passed} frontend error checks passed.`);
} finally {globalThis.fetch=original;}
