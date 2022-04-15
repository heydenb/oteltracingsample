namespace Caller
{
    using System.Diagnostics;

    using OpenTelemetry;
    using OpenTelemetry.Trace;
    using OpenTelemetry.Resources;
    using OpenTelemetry.Context.Propagation;
    using System;
    using System.IO;
    using Dynatrace.OneAgent.Sdk.Api;

    class Program
    {
        public static ActivitySource MainActivitySource;

        public static IOneAgentSdk oneAgentSdk = OneAgentSdkFactory.CreateInstance();

        //public static ITracer MainTracer;

        static void Main(string[] args)
        {
            var serviceName = "OTELCaller";
            var serviceVersion = "1.0.0";

            List<KeyValuePair<string, object>> dt_metadata = new List<KeyValuePair<string, object>>();
            foreach (string name in new string[] {"dt_metadata_e617c525669e072eebe3d0f08212e8f2.properties", "/var/lib/dynatrace/enrichment/dt_metadata.properties"}) {
                try {
                    foreach (string line in System.IO.File.ReadAllLines(name.StartsWith("/var") ? name : System.IO.File.ReadAllText(name))) {
                        var keyvalue = line.Split("=");
                        dt_metadata.Add( new KeyValuePair<string, object>(keyvalue[0], keyvalue[1]));
                    }
                }
                catch { }
            }

            using var tracerProvider = Sdk.CreateTracerProviderBuilder()
                .SetSampler(new AlwaysOnSampler())
                .AddSource(serviceName)
                .SetResourceBuilder(
                    ResourceBuilder.CreateDefault()
                        .AddService(serviceName: serviceName, serviceVersion: serviceVersion)
                        .AddAttributes(dt_metadata))
                .AddConsoleExporter()
                .Build();
            
            MainActivitySource = new ActivitySource(serviceName);
            
            var fl = new FileListener();
            fl.StartListening();

            
            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

    }
}
