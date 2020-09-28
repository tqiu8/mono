const {webkit, chromium, firefox} = require('playwright');
const { setup: setupDevServer } = require('jest-process-manager')

const fs = require('file-system');
const chai = require('chai');
const expect = chai.expect

let page, browser, context

describe("The WebAssembly Core Bindings Test Suite", function(){
  // const DEFAULT_TIMEOUT = 1000;

  beforeAll(async() =>{
    await setupDevServer({
      command: `http-server ./publish -p 8889`,
      port: 8889
    });

    browser = await chromium.launch({headless: false});
    context = await browser.newContext()
    await context.addInitScript(fs.readFileSync('./publish/runtime.js', 'utf8'));
    page = await context.newPage();
    page.once('load', async() => {
      await page.evaluate(() => console.log("runtime done"));
      await page.evaluate(() => document.onRuntimeDone = function() 
      {
        window.doneState = "done";
      })       
    });
    await page.goto('http://localhost:8889/core-bindings-spec.html');
    return await page.waitForFunction(() => {
      return window.doneState === "done"
    });
  })

  test('BindingTestSuite: Should return new Uint8ClampedArray from a c# byte array.', async () => {
    await page.evaluate(() => {clamped = document.Module.BINDING.call_static_method("[BindingsTestSuite]BindingsTestSuite.Program:Uint8ClampedArrayFrom", [])})
    expect(await page.evaluate(() =>clamped.length)).to.equal(50);
    expect(await page.evaluate(() => Object.prototype.toString.call(clamped))).to.equal("[object Uint8ClampedArray]")
  }); 

  test('BindingTestSuite: Should return new Uint8Array from a c# byte array.', async () => {
    await page.evaluate(() => {arr = document.Module.BINDING.call_static_method("[BindingsTestSuite]BindingsTestSuite.Program:Uint8ArrayFrom", [])});
    expect(await page.evaluate(() => arr.length)).to.equal(50);
    expect(await page.evaluate(() => Object.prototype.toString.call(arr))).to.equal("[object Uint8Array]")
  })

  test('BindingTestSuite: Should return new Uint16Array from a c# ushort array', async () => {
    await page.evaluate(() => {arr = document.Module.BINDING.call_static_method("[BindingsTestSuite]BindingsTestSuite.Program:Uint16ArrayFrom", [])});
    expect(await page.evaluate(() => arr.length)).to.equal(50);
    expect(await page.evaluate(() => Object.prototype.toString.call(arr))).to.equal("[object Uint16Array]")
  })

  test('BindingTestSuite: Should return new Uint32Array from a c# uint array.', async () => {
    await page.evaluate(() => {arr = document.Module.BINDING.call_static_method("[BindingsTestSuite]BindingsTestSuite.Program:Uint32ArrayFrom", [])});
    expect(await page.evaluate(() => arr.length)).to.equal(50);
    expect(await page.evaluate(() => Object.prototype.toString.call(arr))).to.equal("[object Uint32Array]")
  })

  test('BindingTestSuite: Should return new Int8Array from a c# sbyte array.', async () => {
    await page.evaluate(() => {arr = document.Module.BINDING.call_static_method("[BindingsTestSuite]BindingsTestSuite.Program:Int8ArrayFrom", [])});
    expect(await page.evaluate(() => arr.length)).to.equal(50);
    expect(await page.evaluate(() => Object.prototype.toString.call(arr))).to.equal("[object Int8Array]")
  })

  test('BindingTestSuite: Should return new Int16Array from a c# short array.', async () => {
    await page.evaluate(() => {arr = document.Module.BINDING.call_static_method("[BindingsTestSuite]BindingsTestSuite.Program:Int16ArrayFrom", [])});
    expect(await page.evaluate(() => arr.length)).to.equal(50);
    expect(await page.evaluate(() => Object.prototype.toString.call(arr))).to.equal("[object Int16Array]")
  })
})