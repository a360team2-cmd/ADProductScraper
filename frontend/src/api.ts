export function errorMessage(error:unknown):string {
  if(error instanceof DOMException && (error.name==='TimeoutError'||error.name==='AbortError'))
    return 'The request timed out. Check the backend connection and retry.';
  if(error instanceof TypeError)
    return 'Unable to connect. Make sure the backend is running, then retry.';
  return error instanceof Error ? error.message : 'An unexpected error occurred. Please retry.';
}

export async function request(path:string, options?:RequestInit, timeoutMs=30000):Promise<Response> {
  try {
    const response=await fetch('/api'+path,{...options,signal:AbortSignal.timeout(timeoutMs)});
    if(!response.ok){
      const body:unknown=await response.json().catch(()=>null);
      const problem=body && typeof body==='object' ? body as Record<string,unknown> : {};
      const fallback=response.status===404?'Collection not found. Refresh the collection history.':`Request failed (${response.status}). Please retry.`;
      const message=typeof problem['error']==='string'?problem['error']:typeof problem['title']==='string'?problem['title']:fallback;
      const reference=typeof problem['traceId']==='string'?` Reference: ${problem['traceId']}`:'';
      throw new Error(message+reference);
    }
    return response;
  } catch(error) { throw new Error(errorMessage(error)); }
}

export async function api<T>(path:string,options?:RequestInit):Promise<T>{
  const response=await request(path,options);
  const text=await response.text();
  if(!text)return null as T;
  try { return JSON.parse(text) as T; }
  catch { throw new Error('The backend returned an invalid response. Check that the frontend connects to the correct API.'); }
}

