// Only the disposable test page may be navigated again; input actions are never retried.
export async function navigateFixture({ navigate, ready, waitReady, pause }) {
  for (let attempt=0;attempt<2;attempt++) {
    const result=await navigate();
    if(result.errorText&&result.errorText!=='net::ERR_ABORTED')throw new Error('Fixture navigation failed: '+result.errorText);
    if(await ready())return;
    if(result.errorText==='net::ERR_ABORTED'&&attempt===0){await pause(250);continue;}
    if(!await waitReady())throw new Error('Disposable fixture URL/title was not verified');
    return;
  }
  throw new Error('Disposable fixture did not become ready');
}
