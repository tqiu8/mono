var Module = { 
	onRuntimeInitialized: function () {
		MONO.mono_load_runtime_and_bcl (
			"managed",
			"managed",
			1,
			[ "BindingsTestSuite.dll", "BrowserTestSuite.dll", "HttpTestSuite.dll", "IssuesTestSuite.dll", "Mono.Security.dll", "mscorlib.dll", "netstandard.dll", "System.Core.dll", "System.dll", "System.Drawing.Common.dll", "System.IO.Compression.dll", "System.Net.Http.dll", "System.Net.Http.WebAssemblyHttpHandler.dll", "System.Runtime.Serialization.dll", "System.ServiceModel.Internals.dll", "System.Xml.dll", "WebAssembly.Bindings.dll", "WebAssembly.Net.WebSockets.dll", "WebSocketTestSuite.dll", "ZipTestSuite.dll", "BindingsTestSuite.pdb", "BrowserTestSuite.pdb", "HttpTestSuite.pdb", "IssuesTestSuite.pdb", "Mono.Security.pdb", "mscorlib.pdb", "System.Core.pdb", "System.Drawing.Common.pdb", "System.IO.Compression.pdb", "System.Net.Http.pdb", "System.Net.Http.WebAssemblyHttpHandler.pdb", "System.pdb", "System.Runtime.Serialization.pdb", "System.ServiceModel.Internals.pdb", "System.Xml.pdb", "WebAssembly.Bindings.pdb", "WebAssembly.Net.WebSockets.pdb", "WebSocketTestSuite.pdb", "ZipTestSuite.pdb" ],
			function () {
				console.log("app init");
				App.init ();
			}
		);
	},
};


