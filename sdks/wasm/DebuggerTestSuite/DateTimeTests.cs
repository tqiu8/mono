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
using System.Globalization;

namespace DebuggerTests
{
	public class DateTimeList : DebuggerTestBase {
		DebugTestContext ctx;
		Dictionary<string, string> dicScriptsIdToUrl;
		Dictionary<string, string> dicFileToUrl;
		Dictionary<string, string> SubscribeToScripts (Inspector insp) {
			dicScriptsIdToUrl = new Dictionary<string, string> ();
			dicFileToUrl = new Dictionary<string, string>();
			insp.On("Debugger.scriptParsed", async (args, c) => {
				var script_id = args? ["scriptId"]?.Value<string> ();
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
		public DateTimeList() : base ("debugger-locale-driver.html") {}

		[Fact]
		public async Task CheckThatAllLocaleSourcesAreSent () {
			var insp = new Inspector ();
			//Collect events
			var scripts = SubscribeToScripts(insp);

			await Ready();
			//all sources are sent before runtime ready is sent, nothing to check
			await insp.Ready ();
			Assert.Contains ("dotnet://debugger-test.dll/debugger-datetime-test.cs", scripts.Values);
		}

		async Task<Result> SetBreakpoint (string url_key, int line, int column, bool expect_ok=true)
		{
			var bp1_req = JObject.FromObject(new {
				lineNumber = line,
				columnNumber = column,
				url = dicFileToUrl[url_key],
			});

			var bp1_res = await ctx.cli.SendCommand ("Debugger.setBreakpointByUrl", bp1_req, ctx.token);
			Assert.True (expect_ok ? bp1_res.IsOk : bp1_res.IsErr);

			return bp1_res;
		}

		async Task<JToken> CheckLocalsOnFrame (JToken frame, string script_loc, int line, int column, string function_name, Action<JToken> test_fn = null)
		{
			CheckLocation (script_loc, line, column, ctx.scripts, frame ["location"]);
			Assert.Equal (function_name, frame ["functionName"].Value<string> ());

			return await CheckLocalsOnFrame (frame, test_fn);
		}

		async Task<JToken> CheckLocalsOnFrame (JToken frame, object expected, string label)
		{
			var locals = await GetProperties (frame ["callFrameId"].Value<string> ());
			try {
				await CheckProps (locals, expected, label);
				return locals;
			} catch {
				Console.WriteLine ($"CheckLocalsOnFrame failed for locals: {locals}");
				throw;
			}
		}

		async Task<JToken> CheckLocalsOnFrame (JToken frame, Action<JToken> test_fn)
		{
			var locals = await GetProperties (frame ["callFrameId"].Value<string> ());
			try {
				test_fn (locals);
				return locals;
			} catch {
				Console.WriteLine ($"CheckLocalsOnFrame failed for locals: {locals}");
				throw;
			}
		}

		async Task CheckProps (JToken actual, object exp_o, string label, int num_fields=-1)
		{
			if (exp_o.GetType ().IsArray || exp_o is JArray) {
				if (! (actual is JArray actual_arr)) {
					Assert.True (false, $"[{label}] Expected to get an array here but got {actual}");
					return;
				}

				var exp_v_arr = JArray.FromObject (exp_o);
				Assert.Equal (exp_v_arr.Count, actual_arr.Count ());

				for (int i = 0; i < exp_v_arr.Count; i ++) {
					var exp_i = exp_v_arr [i];
					var act_i = actual_arr [i];

					Assert.True (act_i ["name"]?.Value<string> () == $"[{i}]", $"{label}-[{i}].name");

					await CheckValue (act_i["value"], exp_i, $"{label}-{i}th value");
				}

				return;
			}

			// Not an array
			var exp = exp_o as JObject;
			if (exp == null)
				exp = JObject.FromObject(exp_o);

			num_fields = num_fields < 0 ? exp.Values<JToken>().Count() : num_fields;
			Assert.True(num_fields == actual.Count(), $"[{label}] Number of fields don't match, Expected: {num_fields}, Actual: {actual.Count()}");

			foreach (var kvp in exp) {
				var exp_name = kvp.Key;
				var exp_val = kvp.Value;

				var actual_obj = actual.FirstOrDefault(jt => jt["name"]?.Value<string>() == exp_name);
				if (actual_obj == null) {
					Console.WriteLine($"actual: {actual}, exp_name: {exp_name}, exp_val: {exp_val}");
					Assert.True(actual_obj != null, $"[{label}] Could not find property named '{exp_name}'");
				}

				var actual_val = actual_obj["value"];
				Assert.True(actual_obj != null, $"[{label}] not value found for property named '{exp_name}'");

				if (exp_val.Type == JTokenType.Array) {
					var actual_props = await GetProperties(actual_val["objectId"]?.Value<string>());
					await CheckProps (actual_props, exp_val, $"{label}-{exp_name}");
				} else {
					await CheckValue (actual_val, exp_val, $"{label}#{exp_name}");
				}
			}
		}

		async Task CheckValue (JToken actual_val, JToken exp_val, string label)
		{
			if (exp_val ["type"] == null && actual_val ["objectId"] != null) {
				var new_val = await GetProperties (actual_val ["objectId"].Value<string> ());
				await CheckProps (new_val, exp_val, $"{label}-{actual_val["objectId"]?.Value<string>()}");
				return;
			}

			foreach (var jp in exp_val.Values<JProperty> ()) {
				var exp_val_str = jp.Value.Value<string> ();
				bool null_or_empty_exp_val = String.IsNullOrEmpty (exp_val_str);

				var actual_field_val = actual_val.Values<JProperty> ().FirstOrDefault (a_jp => a_jp.Name == jp.Name);
				var actual_field_val_str = actual_field_val?.Value?.Value<string> ();
				if (null_or_empty_exp_val && String.IsNullOrEmpty (actual_field_val_str))
					continue;

				Assert.True (actual_field_val != null, $"[{label}] Could not find value field named {jp.Name}");

				Assert.True (exp_val_str == actual_field_val_str,
						$"[{label}] Value for json property named {jp.Name} didn't match.\n" +
						$"Expected: {jp.Value.Value<string> ()}\n" +
						$"Actual:   {actual_field_val.Value.Value<string> ()}");
			}
		}

		async Task<JObject> EvaluateAndCheck (string expression, string script_loc, int line, int column, string function_name,
								Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null)
			=> await SendCommandAndCheck (
						JObject.FromObject (new { expression = expression }),
						"Runtime.evaluate", script_loc, line, column, function_name,
						wait_for_event_fn: wait_for_event_fn,
						locals_fn: locals_fn);

		async Task<JObject> SendCommandAndCheck (JObject args, string method, string script_loc, int line, int column, string function_name,
								Func<JObject, Task> wait_for_event_fn = null, Action<JToken> locals_fn = null, string waitForEvent = Inspector.PAUSE)
		{
			var res = await ctx.cli.SendCommand (method, args, ctx.token);
			if (!res.IsOk) {
				Console.WriteLine ($"Failed to run command {method} with args: {args?.ToString ()}\nresult: {res.Error.ToString ()}");
				Assert.True (false, $"SendCommand for {method} failed with {res.Error.ToString ()}");
			}

			var wait_res = await ctx.insp.WaitFor(waitForEvent);

			if (function_name != null)
				Assert.Equal (function_name, wait_res ["callFrames"]?[0]?["functionName"]?.Value<string> ());

			if (script_loc != null)
				CheckLocation (script_loc, line, column, ctx.scripts, wait_res ["callFrames"][0]["location"]);

			if (wait_for_event_fn != null)
				await wait_for_event_fn (wait_res);

			if (locals_fn != null)
				await CheckLocalsOnFrame (wait_res ["callFrames"][0], locals_fn);

			return wait_res;
		}

		async Task<JToken> CheckObjectOnLocals (JToken locals, string name, Action<JToken> test_fn)
		{
			var obj = locals.Where (jt => jt ["name"]?.Value<string> () == name)
					.FirstOrDefault ();
			if (obj == null) {
				Console.WriteLine ($"CheckObjectOnLocals failed with locals: {locals}");
				Assert.True (false, $"Could not find a var with name {name} and type object");
			}

			var objectId = obj ["value"]["objectId"]?.Value<string> ();
			Assert.True (!String.IsNullOrEmpty (objectId), $"No objectId found for {name}");

			var props = await GetProperties (objectId);
			if (test_fn != null) {
				try {
					test_fn (props);
				} catch (Exception) {
					Console.WriteLine ($"Failed for properties: {props}");
					throw;
				}
			}

			return props;
		}

		async Task<JToken> GetProperties (string id)
		{
			var get_prop_req = JObject.FromObject (new {
				objectId = id
			});

			var frame_props = await ctx.cli.SendCommand ("Runtime.getProperties", get_prop_req, ctx.token);
			if (!frame_props.IsOk)
				Assert.True (false, $"Runtime.getProperties failed for {get_prop_req.ToString ()}, with Result: {frame_props}");

			var locals = frame_props.Value ["result"];
			return locals;
		}

		async Task CheckDateTime (JToken locals, string name, DateTime expected)
			=> await CheckObjectOnLocals (locals, name,
				test_fn: (members) => {
					// not checking everything
#if false
					CheckNumber (members, "Year", expected.Year);
					CheckNumber (members, "Month", expected.Month);
					CheckNumber (members, "Day", expected.Day);
					CheckNumber (members, "Hour", expected.Hour);
					CheckNumber (members, "Minute", expected.Minute);
					CheckNumber (members, "Second", expected.Second);
#endif

					CheckString (members, "Year", "int");
					CheckString (members, "Month", "int");
					CheckString (members, "Day", "int");
					CheckString (members, "Hour", "int");
					CheckString (members, "Minute", "int");
					CheckString (members, "Second", "int");

					// FIXME: check some float properties too
				}
			);

		[Theory]
		[InlineData ("en-US")]
		[InlineData ("es-ES")]
		[InlineData ("de-DE")]
		[InlineData ("ja-JP")]
		[InlineData ("ka-GE")]
		[InlineData ("hu-HU")]
		public async Task CheckDateTimeLocale (string locale) {
			var insp = new Inspector ();
			var scripts = SubscribeToScripts(insp);

			await Ready();
			await insp.Ready (async (cli, token) => {
				ctx = new DebugTestContext (cli, insp, token, scripts);
				var debugger_test_loc = "dotnet://debugger-test.dll/debugger-datetime-test.cs";

				await SetBreakpoint (debugger_test_loc, 20, 3);
				
				var pause_location = await EvaluateAndCheck (
					"window.setTimeout(function() { invoke_static_method ('[debugger-test] DebuggerTests.DateTimeTest:LocaleTest'," 
					+ $"'{locale}'); }}, 1);",
					debugger_test_loc, 20, 3, "LocaleTest",
					locals_fn: (locals) => {
						CultureInfo.CurrentCulture = new CultureInfo (locale, false);
						DateTime dt = new DateTime (2020, 1, 2, 3, 4, 5);
						string dt_str = dt.ToString();

						DateTimeFormatInfo dtfi = CultureInfo.GetCultureInfo(locale).DateTimeFormat;
						var fdtp = dtfi.FullDateTimePattern;
						var ldp = dtfi.LongDatePattern;
						var ltp = dtfi.LongTimePattern;
						var sdp = dtfi.ShortDatePattern;
						var stp = dtfi.ShortTimePattern;

						CheckString(locals, "fdtp", fdtp);
						CheckString(locals, "ldp", ldp);
						CheckString(locals, "ltp", ltp);
						CheckString(locals, "sdp", sdp);
						CheckString(locals, "stp", stp);
						CheckDateTime(locals, "dt", dt);
						CheckString(locals, "dt_str", dt_str);
					}
				);
				
				
			});
		}

	}
}