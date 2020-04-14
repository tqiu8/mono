using System;
using System.Linq;
using System.Threading.Tasks;

using System.Net.WebSockets;
using System.Threading;
using System.IO;
using System.Text;
using System.Collections.Generic;

using Microsoft.Extensions.Logging;
using WebAssembly.Net.Debugging;
using Newtonsoft.Json.Linq;
using Xunit;

namespace DebuggerTests
{
	class Inspector
	{
		// InspectorClient client;
		Dictionary<string, TaskCompletionSource<JObject>> notifications = new Dictionary<string, TaskCompletionSource<JObject>> ();
		Dictionary<string, Func<JObject, CancellationToken, Task>> eventListeners = new Dictionary<string, Func<JObject, CancellationToken, Task>> ();

		public const string PAUSE = "pause";
		public const string READY = "ready";

		public Task<JObject> WaitFor(string what) {
			if (notifications.ContainsKey (what))
				throw new Exception ($"Invalid internal state, waiting for {what} while another wait is already setup");
			var n = new TaskCompletionSource<JObject> ();
			notifications [what] = n;
			return n.Task;
		}

		void NotifyOf (string what, JObject args) {
			if (!notifications.ContainsKey (what))
				throw new Exception ($"Invalid internal state, notifying of {what}, but nobody waiting");
			notifications [what].SetResult (args);
			notifications.Remove (what);
		}

		public void On(string evtName, Func<JObject, CancellationToken, Task> cb) {
			eventListeners[evtName] = cb;
		}

		void FailAllWaitersWithException (JObject exception)
		{
			foreach (var tcs in notifications.Values)
				tcs.SetException (new ArgumentException (exception.ToString ()));
		}

		async Task OnMessage(string method, JObject args, CancellationToken token)
		{
			//System.Console.WriteLine("OnMessage " + method + args);
			switch (method) {
			case "Debugger.paused":
				NotifyOf (PAUSE, args);
				break;
			case "Mono.runtimeReady":
				NotifyOf (READY, args);
				break;
			case "Runtime.consoleAPICalled":
				Console.WriteLine ("CWL: {0}", args? ["args"]? [0]? ["value"]);
				break;
			}
			if (eventListeners.ContainsKey (method))
				await eventListeners[method](args, token);
			else if (String.Compare (method, "Runtime.exceptionThrown") == 0)
				FailAllWaitersWithException (args);
		}

		public async Task Ready (Func<InspectorClient, CancellationToken, Task> cb = null, TimeSpan? span = null) {
			using (var cts = new CancellationTokenSource ()) {
				cts.CancelAfter (span?.Milliseconds ?? 60 * 1000); //tests have 1 minute to complete by default
				var uri = new Uri ($"ws://{TestHarnessProxy.Endpoint.Authority}/launch-chrome-and-connect");
				using var loggerFactory = LoggerFactory.Create(
					builder => builder.AddConsole().AddFilter(null, LogLevel.Trace));
				using (var client = new InspectorClient (loggerFactory.CreateLogger<Inspector>())) {
					await client.Connect (uri, OnMessage, async token => {
						Task[] init_cmds = {
							client.SendCommand ("Profiler.enable", null, token),
							client.SendCommand ("Runtime.enable", null, token),
							client.SendCommand ("Debugger.enable", null, token),
							client.SendCommand ("Runtime.runIfWaitingForDebugger", null, token),
							WaitFor (READY),
						};
						// await Task.WhenAll (init_cmds);
						Console.WriteLine ("waiting for the runtime to be ready");
						await init_cmds [4];
						Console.WriteLine ("runtime ready, TEST TIME");
						if (cb != null) {
							Console.WriteLine("await cb(client, token)");
							await cb(client, token);
						}

					}, cts.Token);
					await client.Close (cts.Token);
				}
			}
		}
	}

	public class DebuggerTestBase {
		protected Task startTask;
		private string testDriver;

		static string FindTestPath (string driver="debugger-driver.html") {
			//FIXME how would I locate it otherwise?
			var test_path = Environment.GetEnvironmentVariable ("TEST_SUITE_PATH");
			//Lets try to guest
			if (test_path != null && Directory.Exists (test_path))
				return test_path;

			var cwd = Environment.CurrentDirectory;
			Console.WriteLine ("guessing from {0}", cwd);
			//tests run from DebuggerTestSuite/bin/Debug/netcoreapp2.1
			var new_path = Path.Combine (cwd, "../../../../bin/debugger-test-suite");
			if (File.Exists (Path.Combine (new_path, driver)))
				return new_path;

			throw new Exception ("Missing TEST_SUITE_PATH env var and could not guess path from CWD");
		}

		static string[] PROBE_LIST = {
			"/Applications/Google Chrome Canary.app/Contents/MacOS/Google Chrome Canary",
			"/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
			"/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
			"/usr/bin/chromium",
			"/usr/bin/chromium-browser",
		};
		static string chrome_path;

		static string FindChromePath ()
		{
			if (chrome_path != null)
				return chrome_path;

			foreach (var s in PROBE_LIST){
				if (File.Exists (s)) {
					chrome_path = s;
					Console.WriteLine($"Using chrome path: ${s}");
					return s;
				}
			}
			throw new Exception ("Could not find an installed Chrome to use");
		}

		public DebuggerTestBase (string driver = "debugger-driver.html") {
			testDriver = driver;
			Console.WriteLine("Test driver from {0}", testDriver);
			startTask = TestHarnessProxy.Start (FindChromePath (), FindTestPath (testDriver), testDriver);
		}

		public Task Ready ()
			=> startTask;

		public void CheckNumber (JToken locals, string name, int value) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;
				var val = l["value"];
				Assert.Equal ("number", val ["type"]?.Value<string> ());
				Assert.Equal (value, val["value"]?.Value <int> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public void CheckString (JToken locals, string name, string value) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;
				var val = l["value"];
				if (value == null) {
						Assert.Equal ("object", val ["type"]?.Value<string> ());
						Assert.Equal ("null", val["subtype"]?.Value<string> ());
				} else {
						Assert.Equal ("string", val ["type"]?.Value<string> ());
						Assert.Equal (value, val["value"]?.Value <string> ());
				}
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public void CheckLocation (string script_loc, int line, int column, Dictionary<string, string> scripts, JToken location)
		{
			var loc_str = $"{ scripts[location["scriptId"].Value<string>()] }"
							+ $"#{ location ["lineNumber"].Value<int> () }"
							+ $"#{ location ["columnNumber"].Value<int> () }";

			var expected_loc_str = $"{script_loc}#{line}#{column}";
			Assert.Equal (expected_loc_str, loc_str);
		}

		public JToken GetAndAssertObjectWithName (JToken obj, string name)
		{
			var l = obj.FirstOrDefault (jt => jt ["name"]?.Value<string> () == name);
			if (l == null)
				Assert.True (false, $"Could not find variable '{name}'");
			return l;
		}

		public void CheckFunction (JToken locals, string name, string description, string subtype=null) {
			Console.WriteLine ($"** Locals: {locals.ToString ()}");
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("function", val ["type"]?.Value<string> ());
				Assert.Equal (description, val ["description"]?.Value<string> ());
				Assert.Equal (subtype, val ["subtype"]?.Value<string> ());
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}

		public JToken CheckObject (JToken locals, string name, string class_name, string subtype=null, bool is_null=false) {
			var l = GetAndAssertObjectWithName (locals, name);
			var val = l["value"];
			Assert.Equal ("object", val ["type"]?.Value<string> ());
			Assert.True (val ["isValueType"] == null || !val ["isValueType"].Value<bool> ());
			Assert.Equal (class_name, val ["className"]?.Value<string> ());

			var has_null_subtype = val ["subtype"] != null && val ["subtype"]?.Value<string> () == "null";
			Assert.Equal (is_null, has_null_subtype);
			if (subtype != null)
				Assert.Equal (subtype, val ["subtype"]?.Value<string> ());

			return l;
		}

		public JToken CheckBool (JToken locals, string name, bool expected)
		{
			var l = GetAndAssertObjectWithName (locals, name);
			var val = l["value"];
			Assert.Equal ("boolean", val ["type"]?.Value<string> ());
			if (val ["value"] == null)
				Assert.True (false, "expected bool value not found for variable named {name}");
			Assert.Equal (expected, val ["value"]?.Value<bool> ());

			return l;
		}

		public void CheckContentValue (JToken token, string value) {
			var val = token["value"].Value<string> ();
			Assert.Equal (value, val);
		}

		public JToken CheckValueType (JToken locals, string name, string class_name) {
			var l = GetAndAssertObjectWithName (locals, name);
			var val = l["value"];
			Assert.Equal ("object", val ["type"]?.Value<string> ());
			Assert.True (val ["isValueType"] != null && val ["isValueType"].Value<bool> ());
			Assert.Equal (class_name, val ["className"]?.Value<string> ());
			return l;
		}

		public JToken CheckEnum (JToken locals, string name, string class_name, string descr) {
			var l = GetAndAssertObjectWithName (locals, name);
			var val = l["value"];
			Assert.Equal ("object", val ["type"]?.Value<string> ());
			Assert.True (val ["isEnum"] != null && val ["isEnum"].Value<bool> ());
			Assert.Equal (class_name, val ["className"]?.Value<string> ());
			Assert.Equal (descr, val ["description"]?.Value<string> ());
			return l;
		}

		public void CheckArray (JToken locals, string name, string class_name) {
			foreach (var l in locals) {
				if (name != l["name"]?.Value<string> ())
					continue;

				var val = l["value"];
				Assert.Equal ("object", val ["type"]?.Value<string> ());
				Assert.Equal ("array", val ["subtype"]?.Value<string> ());
				Assert.Equal (class_name, val ["className"]?.Value<string> ());

				//FIXME: elements?
				return;
			}
			Assert.True(false, $"Could not find variable '{name}'");
		}
	}
} 
