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
    using Dynatrace.OneAgent.Sdk.Api.Enums;
    using Dynatrace.OneAgent.Sdk.Api.Infos;

    class FileListener
    {

        private readonly TextMapPropagator _propagator = Propagators.DefaultTextMapPropagator;

        private static Random random = new Random();  

        public void StartListening()
        {
            using var watcher = new FileSystemWatcher(@"/home/heydenb/workspace/drop");

            watcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            watcher.Changed += OnChanged;
            watcher.Created += OnCreated;
            watcher.Deleted += OnDeleted;
            watcher.Renamed += OnRenamed;
            watcher.Error += OnError;

            watcher.Filter = "*.txt";
            watcher.IncludeSubdirectories = false;
            watcher.EnableRaisingEvents = true;

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            using var activity = Program.MainActivitySource.StartActivity("OnChanged");
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }
            Console.WriteLine($"Changed: {e.FullPath}");
        }

        private void OnCreated(object sender, FileSystemEventArgs e)
        {
            using var activity = Program.MainActivitySource.StartActivity("OnCreated");
            string value = $"Created: {e.FullPath}";
            Console.WriteLine(value);
        }

        private void OnDeleted(object sender, FileSystemEventArgs e) =>
            Console.WriteLine($"Deleted: {e.FullPath}");

        private void OnRenamed(object sender, RenamedEventArgs e)
        {
            using var activity = Program.MainActivitySource.StartActivity("OnRenamed", ActivityKind.Producer);
            Console.WriteLine($"Renamed:");
            Console.WriteLine($"    Old: {e.OldFullPath}");
            Console.WriteLine($"    New: {e.FullPath}");

            var basicProps = new Dictionary<string, string>();
            // Inject the ActivityContext into the message headers to propagate trace context to the receiving service.
            ActivityContext contextToInject = default;
            if (activity != null)
            {
                contextToInject = activity.Context;
            }
            else if (Activity.Current != null)
            {
                contextToInject = Activity.Current.Context;
            }
            _propagator.Inject(
                new PropagationContext(contextToInject, Baggage.Current),
                basicProps,
                InjectTraceContextIntoDictionary);
            
            basicProps.ToList().ForEach(x => {Console.WriteLine(x.Key); Console.WriteLine(x.Value);});

            IOutgoingRemoteCallTracer outgoingRemoteCallTracer = Program.oneAgentSdk.TraceOutgoingRemoteCall(
                "RemoteOnFileChanged", "RemoteOTELCallee",
                "sfrv2://endpoint/service", ChannelType.TCP_IP, "wherever:1234");
            outgoingRemoteCallTracer.SetProtocolName("NServiceBus");

            outgoingRemoteCallTracer.Start();
            try
            {
                string tag = outgoingRemoteCallTracer.GetDynatraceStringTag();
                basicProps["baggage"]=basicProps["baggage"] + "," + string.Format("dtSDKTag={0}", tag);
                WriteTraceContextToFile(basicProps);
            }
            catch (Exception ex)
            {
                outgoingRemoteCallTracer.Error(ex);
                // handle or rethrow
                throw ex;
            }
            finally
            {
                outgoingRemoteCallTracer.End();
            }
        }

        private void OnError(object sender, ErrorEventArgs e) =>
            PrintException(e.GetException());

        private void PrintException(Exception? ex)
        {
            if (ex != null)
            {
                Console.WriteLine($"Message: {ex.Message}");
                Console.WriteLine("Stacktrace:");
                Console.WriteLine(ex.StackTrace);
                Console.WriteLine();
                PrintException(ex.InnerException);
            }
        }   

        private static void InjectTraceContextIntoDictionary(
            Dictionary<string, string> props, string key, string value)
        {
            if (key.Equals("X-dynaTrace")){
                props["baggage"] = string.Format("X-dynaTrace={0}", value);
            }
            props[key] = value;
        }

        private static void WriteTraceContextToFile(Dictionary<string, string> props){
            using (StreamWriter file = new StreamWriter(string.Format(@"/home/heydenb/workspace/drop/protocol-{0}.out", random.NextDouble().ToString())))
                foreach (var entry in props){
                    file.WriteLine("{0}:{1}", entry.Key, entry.Value);
                }
        }
    }
}
