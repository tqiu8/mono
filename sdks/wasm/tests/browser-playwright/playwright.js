const {webkit, chromium, firefox} = require('playwright');
const fs = require('file-system');

// const files = [
//     {name: './publish/http-spec.html', mimeType: 'text/html'},
//     {name: './publish/http-spec.js', mimeType: 'text/javascript'},
//     {name: './publish/http-spec-with-prom-lib.js', mimeType: 'text/javascript'},
//     {name: './publish/core-bindings-spec.js', mimeType: 'text/javascript'},
//     {name: './publish/issues-spec.js', mimeType: 'text/javascript'},
//     {name: './publish/zip-spec.js', mimeType: 'text/javascript'},
//     {name: './publish/dotnet.wasm'},
//     {name: './publish/dotnet.worker.js', mimeType: 'text/javascript'},
//     {name: './publish/dotnet.js.mem'},
//     {name: './publish/dotnet.js', mimeType: 'text/javascript'},
//     {name: './publish/mono-config.js', mimeType: 'text/javascript'},
//     {name: './publish/runtime.js', mimeType: 'text/javascript'},
//     {name: './publish/managed/*.dll'},
//     {name: './publish/managed/*.pdb'},
//     {name: './publish/**/*.txt', mimeType: 'text'},
//     {name: './publish/**/*.zip', mimeType: 'zip'},
//     {name: './publish/**/*.nupkg', mimeType: 'zip'},
// ]

(async () => {
    const browserServer = await chromium.launchServer({port: 8889 ,headless: false});
    const wsEndpoint = browserServer.wsEndpoint();
    // Use web socket endpoint later to establish a connection.
    const browser = await chromium.connect({ wsEndpoint });
    const page = await browser.newPage();
    const file = await fs.readFileSync('./publish/http-spec.html', 'utf-8');
    const script = await fs.readFileSync('./publish/http-spec.js', 'utf-8')
    page.setContent(file)
    page.addInitScript(script);
    await page.goto("./publish/http-spec.html")
    // await browserServer.close();
  })();