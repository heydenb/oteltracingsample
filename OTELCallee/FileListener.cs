namespace Callee
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

        private FileSystemWatcher watcher = new FileSystemWatcher(@"/home/heydenb/workspace/valueblue/drop");

        public void StartListening()
        {

            watcher.NotifyFilter = NotifyFilters.Attributes
                                 | NotifyFilters.CreationTime
                                 | NotifyFilters.DirectoryName
                                 | NotifyFilters.FileName
                                 | NotifyFilters.LastAccess
                                 | NotifyFilters.LastWrite
                                 | NotifyFilters.Security
                                 | NotifyFilters.Size;

            watcher.Changed += OnChanged;

            watcher.Filter = "*.out";
            watcher.IncludeSubdirectories = false;
            watcher.EnableRaisingEvents = true;

            Console.WriteLine("Press enter to exit.");
            Console.ReadLine();
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }
            watcher.EnableRaisingEvents = false;
            Console.WriteLine($"Changed: {e.FullPath}");

            var otelProps = new Dictionary<string, string>();
            using (var sr = new StreamReader(e.FullPath))
            {
                // Read the stream as a string, and write the string to the console.
                while (sr.Peek() >= 0)
                {
                    var line = sr.ReadLine();
                    var ls = line.Split(':');
                    otelProps[ls[0]]=ls[1];
                }
            }

            var parentContext = _propagator.Extract(default, otelProps, ExtractTraceContextFromBasicProperties);
            
            IIncomingRemoteCallTracer incomingRemoteCallTracer = Program.oneAgentSdk
            .TraceIncomingRemoteCall("RemoteOnFileChanged", "RemoteOTELCallee", "sfrv2://endpoint/service");

            string incomingDynatraceStringTag = parentContext.Baggage.GetBaggage("dtSDKTag"); // retrieve from incoming call metadata
            // link both sides of the remote call together
            incomingRemoteCallTracer.SetDynatraceStringTag(incomingDynatraceStringTag);
            incomingRemoteCallTracer.SetProtocolName("ServiceFabricRemotingV2");

            incomingRemoteCallTracer.Start();
            try
            {
                //ProcessRemoteCall();
            

                //otelProps.ToList().ForEach(x => {Console.WriteLine(x.Key); Console.WriteLine(x.Value);});

                /*
                using (var myChildActivity = Program.MainActivitySource.StartActivity(this.CreateActivity("ArrivedInCallee", otelProps), ActivityKind.Server)){
                    Console.WriteLine("Do stuff at client");
                    Thread.Sleep(1000);
                }*/
                    
                var myChildActivity = Program.MainActivitySource.CreateActivity(
                "ArrivedInCallee", ActivityKind.Server, parentContext.ActivityContext);
                
                //myChildActivity.SetParentId(parentContext.ActivityContext.TraceId, parentContext.ActivityContext.SpanId, parentContext.ActivityContext.TraceFlags);
                //myChildActivity.TraceStateString = parentContext.ActivityContext.TraceState;

                myChildActivity?.AddTag("X-dynaTrace", parentContext.Baggage.GetBaggage("X-dynaTrace"));
                myChildActivity?.AddBaggage("X-dynaTrace", parentContext.Baggage.GetBaggage("X-dynaTrace"));
                myChildActivity.Start();

                Console.WriteLine("Do stuff at client");
                Thread.Sleep(1000);

                myChildActivity.Stop();
            
            }
            catch (Exception ex)
            {
                incomingRemoteCallTracer.Error(ex);
                // handle or rethrow
                throw ex;
            }
            finally
            {
                incomingRemoteCallTracer.End();
            }

            watcher.EnableRaisingEvents = true;
        }

        private Activity CreateActivity(String ActivityName, Dictionary<string, string> otelProps){
            var textMapPropagator = Propagators.DefaultTextMapPropagator;
            if (textMapPropagator is not TraceContextPropagator){
                var ctx = textMapPropagator.Extract(default, otelProps, ExtractTraceContextFromBasicProperties);
            
                if (ctx.ActivityContext.IsValid()){
                    // Create a new activity with its parent set from the extracted context.
                    // This makes the new activity as a "sibling" of the activity created by
                    // Asp.Net Core.
                    Activity newOne = new Activity(ActivityName);
                    newOne.SetParentId(ctx.ActivityContext.TraceId, ctx.ActivityContext.SpanId, ctx.ActivityContext.TraceFlags);
                    newOne.TraceStateString = ctx.ActivityContext.TraceState;

                    newOne.SetTag("IsCreatedByInstrumentation", bool.TrueString);

                    // Starting the new activity make it the Activity.Current one.
                    //newOne.Start();
                    Baggage.Current = ctx.Baggage;
                    Console.WriteLine("newOne Created");

                    return newOne;
                }
                else {
                    Console.WriteLine("ctx invalid");
                }
                Console.WriteLine("textMapPropagator is TraceContextPropagator");
            }
            return null;
        }

        private IEnumerable<string> ExtractTraceContextFromBasicProperties(Dictionary<string, string> props, string key)
        {
            Console.WriteLine("key=" + key);
            if (props.ContainsKey(key)){
                Console.WriteLine("value=" + props[key]);
                return new[] {props[key]};
            }
            return Enumerable.Empty<string>();
        }

    }
}