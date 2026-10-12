// Render and exercise only the local design mockups. No application or archive operations.
const fs = require('node:fs');
const path = require('node:path');
const {execFileSync} = require('node:child_process');
const runtime = process.env.ZXTRACT_DESIGN_RUNTIME || 'C:/Users/excel/.cache/codex-runtimes/codex-primary-runtime/dependencies';
const {chromium} = require(path.join(runtime, 'node/node_modules/playwright'));
const python = path.join(runtime, 'python/python.exe');
const skillRoot = 'C:/Users/excel/.codex/plugins/cache/openai-bundled/visualize/1.0.46/skills/visualize';
const source = path.join(__dirname, 'zxtract-directions.html');
const output = path.join(__dirname, 'previews');
const html = execFileSync(python, ['-X','utf8',path.join(skillRoot,'scripts/render.py'), source], {encoding:'utf8',maxBuffer:10*1024*1024});
const lucide = fs.readFileSync(path.join(runtime,'node/node_modules/lucide/dist/umd/lucide.js'),'utf8');
const assert = (condition,message) => {if(!condition) throw new Error(message);};

(async()=>{
 fs.mkdirSync(output,{recursive:true});
 const browser=await chromium.launch({headless:true,executablePath:process.env.ZXTRACT_DESIGN_CHROMIUM || 'C:/Users/excel/AppData/Local/ms-playwright/chromium_headless_shell-1228/chrome-headless-shell-win64/chrome-headless-shell.exe'});
 const page=await browser.newPage({viewport:{width:1432,height:1300},deviceScaleFactor:1});
 const errors=[];
 page.on('pageerror',error=>errors.push(error.message));
 // The mockup has no network operations. The wrapper's optional tooltip scripts
 // are not needed for these checks and are deliberately not fetched.
 await page.route('https://**/*',route=>route.abort());
 const reports=[];
 async function load(width=1400){
  await page.setViewportSize({width:width+32,height:1600});
  await page.setContent(html,{waitUntil:'load'});
  const frame=page.frames().find(f=>f!==page.mainFrame());
  await frame.waitForSelector('.zx-window');
  await frame.addScriptTag({content:lucide});
  await frame.evaluate(()=>lucide.createIcons({attrs:{width:16,height:16}}));
  return frame;
 }
 async function show(frame,index){
  for(let i=0;i<index;i++) await frame.locator('.viz-carousel-next').click();
  await frame.locator('[data-variant]:not([hidden]) .zx-window').waitFor();
 }
 for(const [index,theme] of ['ink','paper','grid'].entries()){
  const frame=await load(1024); await show(frame,index);
  const win=frame.locator(`[data-theme="${theme}"] .zx-window`);
  assert(await win.locator('tbody tr').count()===6,theme+': initial row count');
  assert(await win.locator('tbody [role=progressbar]').count()===1,theme+': unknown password progress was shown as a percent');
  assert(await win.locator('tbody [role=progressbar]').getAttribute('aria-valuenow')==='63',theme+': extraction percentage');
  await win.locator('[data-cmd=pause]').click();
  assert((await win.locator('.zx-session').textContent()).includes('已暂停调度'),theme+': pause response');
  assert((await win.locator('.zx-pause-hint').textContent()).includes('会继续'),theme+': pause semantics');
  assert(await win.locator('tbody .zx-selected [data-cmd=select]').getAttribute('data-id')==='1',theme+': selection changed when paused');
  await win.locator('[data-cmd=pause]').click();
  await win.locator('[data-filter=attention]').click();
  assert(await win.locator('tbody [data-row]').count()===1,theme+': attention count');
  assert((await win.locator('.zx-inspector').textContent()).includes('文档合集.rar'),theme+': stale selection after filtering');
  await win.locator('[data-cmd=retry]').click();
  assert(await win.locator('textarea').isVisible(),theme+': password retry panel');
  assert(await win.locator('textarea').evaluate(el=>document.activeElement===el),theme+': retry keyboard focus');
  await win.locator('[data-cmd=retry-confirm]').click();
  assert((await win.locator('.zx-toast').textContent()).includes('请先输入'),theme+': required password validation');
  await win.locator('textarea').fill('example-password');
  await win.locator('[data-cmd=retry-confirm]').click();
  assert(await win.locator('tbody [data-row]').count()===0,theme+': attention filter after retry');
  await win.locator('[data-filter=all]').click();
  await win.locator('.zx-search input').fill('no-such-archive');
  assert((await win.locator('.zx-empty').textContent()).includes('没有匹配'),theme+': search empty state');
  await win.locator('.zx-search input').fill('');
  await win.locator('.zx-search input').fill('D:\\资料\\待解压');
  assert(await win.locator('tbody [data-row]').count()===6,theme+': full path search');
  await win.locator('.zx-search input').fill('');
  await win.locator('[data-cmd=select][data-id="2"]').click();
  assert((await win.locator('.zx-inspector').textContent()).includes('照片归档.7z'),theme+': inspector selection');
  await win.locator('tbody [data-cmd=copy][data-id="2"]').click();
  assert((await win.locator('tbody [data-cmd=copy][data-id="2"]').textContent()).includes('已复制'),theme+': copy feedback');
  await win.locator('[data-cmd=options]').first().click();
  assert(await win.locator('.zx-options-grid').isVisible(),theme+': option panel');
  await win.locator('[data-cmd=password]').first().click();
  assert(!await win.locator('.zx-options-grid').isVisible()&&await win.locator('textarea').isVisible(),theme+': panels overlap');
  await win.locator('[data-cmd=close-panel]').filter({visible:true}).click();
  await win.locator('[data-cmd=cancel]').click();
  assert(await win.locator('dialog').isVisible(),theme+': cancel confirmation');
  await win.locator('[data-cmd=confirm-cancel]').click();
  assert((await win.locator('.zx-session').textContent()).includes('已完成 1 / 6'),theme+': completed count lost after cancellation');
  assert(await win.locator('[data-cmd=pause]').isDisabled(),theme+': canceled controls remain enabled');
  reports.push({theme,interactions:'PASS'});
 }
 for(const width of [1400,1024,736,320]){
  const frame=await load(width);
  for(const [index,theme] of ['ink','paper','grid'].entries()){
   if(index>0)await frame.locator('.viz-carousel-next').click();
   const win=frame.locator(`[data-theme="${theme}"] .zx-window`);
   const layout=await win.evaluate(el=>{
    const bounds=el.getBoundingClientRect();
    const bad=[...el.querySelectorAll('button,input,select,textarea,td,.zx-inspector,.zx-search,.zx-overview')].filter(node=>{
     const rect=node.getBoundingClientRect();
     return rect.width&&rect.height&&(rect.right>bounds.right+1||rect.left<bounds.left-1);
    }).map(node=>({tag:node.tagName,text:node.textContent.slice(0,50)}));
    return {width:bounds.width,height:bounds.height,overflow:bad};
   });
   assert(layout.overflow.length===0,`${theme} ${width}: overflowing controls ${JSON.stringify(layout.overflow)}`);
   await win.screenshot({path:path.join(output,`${theme}-${width}.png`)});
   const contrast=await win.evaluate(el=>{
    const rgb=s=>{const m=s.match(/[\d.]+/g);return m?m.slice(0,3).map(Number):null;};
    const lum=a=>a.map(x=>{x/=255;return x<=.04045?x/12.92:((x+.055)/1.055)**2.4;}).reduce((s,x,i)=>s+x*[.2126,.7152,.0722][i],0);
    const values=[];
    for(const node of el.querySelectorAll('.zx-file-copy strong,.zx-file-copy small,.zx-kicker,.zx-status,.zx-status-note,.zx-param > span,.zx-session,.zx-percentage,.zx-button,.zx-actions button')){
     if(!node.getBoundingClientRect().height)continue;
     let parent=node,bg;
     while(parent&&parent!==el.parentElement){const s=getComputedStyle(parent),m=s.backgroundColor.match(/[\d.]+/g);if(m&&(m.length===3||Number(m[3])>.99)){bg=rgb(s.backgroundColor);break;}parent=parent.parentElement;}
     const fg=rgb(getComputedStyle(node).color);
     if(fg&&bg){const a=lum(fg),b=lum(bg);values.push({text:node.textContent.trim().slice(0,30),ratio:Math.round((Math.max(a,b)+.05)/(Math.min(a,b)+.05)*100)/100});}
    }
    return {min:Math.min(...values.map(v=>v.ratio)),belowAA:values.filter(v=>v.ratio<4.5)};
   });
   assert(contrast.belowAA.length===0,`${theme}: low text contrast ${JSON.stringify(contrast.belowAA)}`);
   reports.push({theme,viewport:width,...layout,contrast});
  }
 }
 const frame=await load(1024);
 const ink=frame.locator('.zx-ink');
 await ink.locator('[data-cmd=pause]').click();
 await ink.screenshot({path:path.join(output,'ink-paused.png')});
 await ink.locator('[data-filter=attention]').click();
 await ink.locator('[data-cmd=retry]').click();
 await ink.screenshot({path:path.join(output,'ink-password-retry.png')});
 await page.emulateMedia({reducedMotion:'reduce'});
 const motion=await ink.locator('button').first().evaluate(el=>getComputedStyle(el).transitionDuration);
 assert(motion==='0s','Reduced-motion preference not honored');
 assert(errors.length===0,'JavaScript errors: '+errors.join('; '));
 fs.writeFileSync(path.join(__dirname,'validation.json'),JSON.stringify({result:'PASS',testedAt:new Date().toISOString(),sourceBytes:fs.statSync(source).size,reducedMotion:'PASS',javascriptErrors:errors,reports},null,2)+'\n');
 console.log(JSON.stringify({result:'PASS',screenshots:14,reports},null,2));
 await browser.close();
})().catch(error=>{console.error(error);process.exit(1);});
