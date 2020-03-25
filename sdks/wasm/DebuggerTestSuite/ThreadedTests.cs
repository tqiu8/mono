using System;
using System.Linq;
using System.Threading.Tasks;

using System.Net.WebSockets;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;

using Newtonsoft.Json.Linq;
using Xunit;
using WebAssembly.Net.Debugging;

namespace DebuggerTests
{

    public class ThreadedSourceList : DebuggerTestBase {
        DebugTestContext ctx;
        Dictionary<string, string> dicScriptsIdToUrl;
		Dictionary<string, string> dicFileToUrl;
        Dictionary<string, string> SubscribeToScripts (Inspector insp) {
            dicScriptsIdToUrl = new Dictionary<string, string> ();
            dicFileToUrl = new Dictionary<string, string>();
            insp.On("Debugger.scriptParsed", async (args, c) => {
                var script_id = args?["scriptId"]?.Value<string> ();
                var url = args["url"]?.Value<string> ();
                if (script_id.StartsWith("dotnet://"))
                {
                    var dbgUrl = args["dotNetUrl"]?.Value<string>();
                    var arrStr = dbgUrl.Split("/");
                    dbgUrl = arrStr[0] + "/" + arrStr[1] + "/" + arrStr[2] + "/" + arrStr[arrStr.Length - 1];
                    dicScriptsIdToUrl[script_id] = dbgUrl;
                    dicFileToUrl[dbgUrl] = args["url"]?.Value<string>();
                } else if (!String.IsNullOrEmpty (url)) {
                    dicFileToUrl[new Uri (url).AbsolutePath] = url;
                } 
                await Task.FromResult (0);
            });
            return dicScriptsIdToUrl;
        }

        public ThreadedSourceList () {
            Environment.SetEnvironmentVariable("TEST_SUITE_PATH", "../../../../bin/threaded-debugger-test-suite");
        }

        void CheckLocation (string script_loc, int line, int column, Dictionary<string, string> scripts, JToken location)
		{
			var loc_str = $"{ scripts[location["scriptId"].Value<string>()] }"
							+ $"#{ location ["lineNumber"].Value<int> () }"
							+ $"#{ location ["columnNumber"].Value<int> () }";

			var expected_loc_str = $"{script_loc}#{line}#{column}";
			Assert.Equal (expected_loc_str, loc_str);
		}

        void CheckNumber (JToken locals, string name, int value) {
            foreach (var l in locals) {
                if (name != l["name"]?.Value<string> ())
                    continue;
                var val = l["value"];
                Assert.Equal ("number", val["type"]?.Value<string> ());
                Assert.Equal (value, val["value"]?.Value<int>());
                return;
            }
            Assert.True(false, $"Could not find variable '{name}'");
        }

        [Fact]
        public async Task CheckThatAllSourcesAreSent () {
            var insp = new Inspector ();
            
            var scripts = SubscribeToScripts (insp);
            await Ready ();
            await insp.Ready ();
            Assert.Contains ("dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs", scripts.Values);
        }

        [Fact]
        public async Task CreateGoodBreakpoint () {
            var insp = new Inspector ();

            var scripts = SubscribeToScripts(insp);

            await Ready();
            await insp.Ready (async (cli, token) => {
                ctx = new DebugTestContext (cli, insp, token, scripts);
                var bp1_res = await SetBreakpoint ("dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs", 14, 8, ctx);
                Assert.Equal ("dotnet:0", bp1_res.Value ["breakpointId"]);                
                Assert.Equal (1, bp1_res.Value ["locations"]?.Value<JArray> ()?.Count);
                
                var loc = bp1_res.Value ["locations"]?.Value<JArray> ()[0];
                Assert.NotNull (loc ["scriptId"]);
                Assert.Equal ("dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs", scripts [loc["scriptId"]?.Value<string> ()]);
                Assert.Equal (14, loc ["lineNumber"]);
                Assert.Equal (8, loc ["columnNumber"]);
            });
        }

        async Task<Result> SetBreakpoint (string url_key, int line, int column, DebugTestContext ctx, bool expect_ok=true)
        {
            var bp1_req = JObject.FromObject(new {
                lineNumber = line,
                columnNumber = column,
                url = dicFileToUrl[url_key],
            });
            var bp1_res = await ctx.cli.SendCommand ("Debugger.setBreakpointByUrl", bp1_req, ctx.token);
            Assert.True (expect_ok ? bp1_res.IsOk : bp1_res.IsErr);
            Console.WriteLine("BREAKPOINT SET");
            return bp1_res;
        }

        async Task<JObject> StepAndCheck (StepKind kind, string script_loc, int line, int column, string function_name, DebugTestContext ctx,
                            Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null, int times = 1)
        {
            for (int i = 0; i < times - 1; i++) {
                await SendCommandAndCheck (null, $"Debugger.step{kind.ToString ()}", null, -1, -1, null, ctx);
            }

            return await SendCommandAndCheck (
                null, $"Debugger.step{kind.ToString ()}", script_loc, line, column, function_name, ctx,
                wait_for_event_fn: wait_for_event_fn,
                locals_fn: locals_fn
            );
        }

        async Task<JObject> EvaluateAndCheck (string expression, string script_loc, int line, int column, string function_name, DebugTestContext ctx,
                                Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null)
            => await SendCommandAndCheck (
                JObject.FromObject (new { expression = expression}),
                "Runtime.evaluate", script_loc, line, column, function_name, ctx,
                wait_for_event_fn: wait_for_event_fn,
                locals_fn: locals_fn);

        async Task<JObject> SendCommandAndCheck (JObject args, string method, string script_loc, int line, int column, string function_name, DebugTestContext ctx,
                                Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null, string waitForEvent = Inspector.PAUSE)
        {
            var res = await ctx.cli.SendCommand (method, args, ctx.token);
            if (!res.IsOk) {
                Console.WriteLine ($"Failed to run command {method} with args: {args?.ToString ()}\nresult: {res.Error.ToString ()}");
                Assert.True (false, $"SendCommand for {method} failed with {res.Error.ToString ()}");
            }
            var wait_res = await ctx.insp.WaitFor(waitForEvent);
            Console.WriteLine("WAIT RES OVER");
            if (script_loc != null) {
                CheckLocation (script_loc, line, column, ctx.scripts, wait_res ["callFrames"][0]["location"]);
            }

            if (wait_for_event_fn != null) {
                await wait_for_event_fn (wait_res);
            }
               
            if (locals_fn != null) {
                await CheckLocalsOnFrame (wait_res ["callFrames"][0], ctx, locals_fn);
            }

            return wait_res;
        }

        async Task CheckLocalsOnFrame (JToken frame, string script_loc, int line, int column, string function_name, DebugTestContext ctx, Action<JToken> test_fn = null)
        {
            CheckLocation (script_loc, line, column, ctx.scripts, frame ["location"]);
            Assert.Equal (function_name, frame ["functionName"].Value<string> ());

            await CheckLocalsOnFrame (frame, ctx, test_fn);
        }

        async Task CheckLocalsOnFrame (JToken frame, DebugTestContext ctx, Action<JToken> test_fn = null)
        {
            var get_prop_req = JObject.FromObject (new {
                objectId = frame["callFrameId"]
            });

            var frame_props = await ctx.cli.SendCommand ("Runtime.getProperties", get_prop_req, ctx.token);
            if (!frame_props.IsOk)
                Assert.True (false, $"Runtime.getProperties failed for {get_prop_req.ToString ()}");
            
            if (test_fn == null)
                return;

            var locals = frame_props.Value ["result"];
            try {
                test_fn (locals);
            } catch {
                Console.WriteLine ($"Failed trying to check locals: {locals.ToString ()}");
                throw;
            }
        }

        [Fact]
        public async Task CreateBreakpointAndHit () {
            var insp = new Inspector ();
            var scripts = SubscribeToScripts(insp);

            await Ready();
            Console.WriteLine("READY 1");
            await insp.Ready (async (cli, token) => {
                ctx = new DebugTestContext (cli, insp, token, scripts);

                await SetBreakpoint ("dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs", 14, 8, ctx);

                var eval_req = JObject.FromObject(new {
                    expression = "window.setTimeout(function() { sleep-test(); }, 1);",
                });

                await EvaluateAndCheck (
                    "window.setTimeout(function() { sleep_test(); }, 1);",
                    "dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs", 14, 8,
                    "SleepTest", ctx,
                    wait_for_event_fn: (pause_location) => {
                        Console.WriteLine("WAIT FOR EVENT");
                        Assert.Equal ("other", pause_location["reason"]?.Value<string> ());
                        Assert.Equal ("dotnet:0", pause_location ["hitBreakpoints"]?[0]?.Value<string> ());

                        var top_frame = pause_location ["callFrames"][0];
                        Assert.Equal ("SleepTest", top_frame ["functionName"].Value<string> ());
                        Assert.Contains ("threaded-debugger-test.cs", top_frame["url"].Value<string> ());

                        CheckLocation ("dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs", 4, 33, scripts, top_frame["functionLocation"]);
                        return Task.CompletedTask;
                    }
                );
            });

        }

        [Fact]
        public async Task InspectLocalsDuringSteppingWithSleep () {
           var insp = new Inspector ();

           var scripts = SubscribeToScripts(insp);

           await Ready();
           await insp.Ready (async (cli, token) => {
               ctx = new DebugTestContext (cli, insp, token, scripts);

               var debugger_test_loc = "dotnet://threaded-debugger-test.dll/threaded-debugger-test.cs";
               await SetBreakpoint (debugger_test_loc, 25, 2, ctx);
               await SetBreakpoint (debugger_test_loc, 32, 2, ctx);
               await SetBreakpoint (debugger_test_loc, 38, 2, ctx);

               await EvaluateAndCheck (
                   "window.setTimeout(function() { sleep_test(); }, 1);",
                   debugger_test_loc, 14, 2, "SleepTest", ctx,
                   locals_fn: (locals) => {
                       CheckNumber (locals, "count", 30);
                   }
               );

            //     await StepAndCheck (StepKind.Over, debugger_test_loc, 25, 2, "MethodA", ctx,
            //         locals_fn: (locals) => {
            //             CheckNumber (locals, "count", 40);
            //         }
            //     );

            //     await StepAndCheck (StepKind.Over, debugger_test_loc, 32, 2, "MethodB", ctx,
            //         locals_fn: (locals) => {
            //             CheckNumber (locals, "count", 60);
            //         }
            //     );
           });
        }
    }
}