const { SERVER } = require("jest-playwright-preset");
const { setup: setupDevServer } = require('jest-process-manager')

module.exports = {
  launchType: SERVER,
  serverOptions: {
    command: `http-server ./publish -p 8889`,
    port: 8889,
  },
  launchOptions: {
    headless: false
  },
  exitOnPageError: true,
  browsers: ['chromium', 'firefox', 'webkit'],
}