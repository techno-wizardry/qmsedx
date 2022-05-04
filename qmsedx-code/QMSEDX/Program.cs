using QMSEDX.QMSAPI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;
using System.ServiceModel.Configuration;
using System.ServiceModel.Description;
using System.ServiceModel.Dispatcher;
using System.Text.RegularExpressions;
using System.Threading;

namespace QMSEDX
{
    class ServiceKeyBehaviorExtensionElement : BehaviorExtensionElement
    {
        public override Type BehaviorType
        {
            get { return typeof(ServiceKeyEndpointBehavior); }
        }

        protected override object CreateBehavior()
        {
            return new ServiceKeyEndpointBehavior();
        }
    }

    class ServiceKeyEndpointBehavior : IEndpointBehavior
    {
        public void Validate(ServiceEndpoint endpoint) { }

        public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters) { }

        public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher) { }

        public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime)
        {
            clientRuntime.MessageInspectors.Add(new ServiceKeyClientMessageInspector());
        }
    }

    class ServiceKeyClientMessageInspector : IClientMessageInspector
    {
        private const string SERVICE_KEY_HTTP_HEADER = "X-Service-Key";

        public static string ServiceKey { get; set; }

        public object BeforeSendRequest(ref Message request, IClientChannel channel)
        {
            if (request.Properties.TryGetValue(HttpRequestMessageProperty.Name, out object httpRequestMessageObject))
            {
                if (httpRequestMessageObject is HttpRequestMessageProperty httpRequestMessage)
                {
                    httpRequestMessage.Headers[SERVICE_KEY_HTTP_HEADER] = (ServiceKey ?? string.Empty);
                }
                else
                {
                    httpRequestMessage = new HttpRequestMessageProperty();
                    httpRequestMessage.Headers.Add(SERVICE_KEY_HTTP_HEADER, (ServiceKey ?? string.Empty));
                    request.Properties[HttpRequestMessageProperty.Name] = httpRequestMessage;
                }
            }
            else
            {
                HttpRequestMessageProperty httpRequestMessage = new HttpRequestMessageProperty();
                httpRequestMessage.Headers.Add(SERVICE_KEY_HTTP_HEADER, (ServiceKey ?? string.Empty));
                request.Properties.Add(HttpRequestMessageProperty.Name, httpRequestMessage);
            }
            return null;
        }

        public void AfterReceiveReply(ref Message reply, object correlationState) { }
    }

    class Program
    {

        #region Task properties

        static private string _taskIdOrName = null;
        static private string _variableName = string.Empty;
        static private List<string> _variableValues = new List<string>();
        static private string _password = string.Empty;
        static private int _timeout = 3600;
        static private int _pollIntervall = 5000;
        static private bool _noWait = false;
        static private TraceLevel _verbosity = TraceLevel.Verbose;
        static private bool _list = false;
        static private int _delay = 0;
        static private Guid _qdsid = Guid.Empty;
        static private IQMS5 _client;
        private static readonly List<EDXStatus> _statuse = new List<EDXStatus>();

        #endregion

        static void Main(string[] args)
        {
            Trace.Listeners.Add(new ConsoleTraceListener());

            if (args.Count() == 0)
            {
                Usage();
                return;
            }
            if (!ParseArguments(args))
            {
                return;
            }

            if (_list) Environment.Exit(ListEdxTasks() ? 0 : 1);
            else Environment.Exit(TriggerAndMonitorTask() ? 0 : 1);
        }

        static bool TriggerAndMonitorTask()
        {
            bool totalResult = false;

            if (CreateQMSAPIClient())
            {
                //Trigger the main task.
                TriggerEDXTaskResult triggerResult = TriggerTask(_taskIdOrName, _password, _variableName, _variableValues);

                if (triggerResult != null)
                {
                    if (triggerResult.EDXTaskStartResult == EDXTaskStartResult.Success)
                    {
                        TraceMsgL(TraceLevel.Info, String.Format("Task \"{0}\" successfully started!", _taskIdOrName));

                        if (!_noWait)
                        {
                            EDXStatus mainTaskResult = PollSingleTask(triggerResult.ExecId, _pollIntervall, _timeout);

                            if (mainTaskResult != null)
                            {
                                _statuse.Add(mainTaskResult);
                                MonitorTriggeredTasks(mainTaskResult.TriggeredTasksId);
                                totalResult = AnalyseResult(mainTaskResult.TaskStatus == TaskStatusValue.Completed);
                            }
                            else
                            {
                                totalResult = false;
                            }
                        }
                        else
                        {
                            TraceMsgL(TraceLevel.Verbose, String.Format("Don't wait for task completion!"));
                            totalResult = true;
                        }
                    }
                    else
                    {
                        TraceMsgL(TraceLevel.Error, "Failed to start the task due to the following error: " + Enum.GetName(typeof(EDXTaskStartResult), triggerResult.EDXTaskStartResult));
                        totalResult = false;
                    }
                }
                else
                {
                    TraceMsgL(TraceLevel.Error, "Failed to start the task!");
                }
            }

            if (_delay > 0) Thread.Sleep(_delay);
            //Console.ReadKey();
            return totalResult;
        }

        static bool AnalyseResult(bool mainTaskStatus)
        {
            bool allTasksSucceded;

            if (mainTaskStatus && !_statuse.Any(status => status.TaskStatus == TaskStatusValue.Failed))
                allTasksSucceded = true;
            else
                allTasksSucceded = false;

            if (_statuse.Count() > 0)
            {

                IEnumerable<EDXStatus> completed = _statuse.Where(s => s.TaskStatus == TaskStatusValue.Completed);
                IEnumerable<EDXStatus> failed = _statuse.Where(s => s.TaskStatus == TaskStatusValue.Failed);
                IEnumerable<EDXStatus> aborted = _statuse.Where(s => s.TaskStatus == TaskStatusValue.Aborting);
                IEnumerable<EDXStatus> warning = _statuse.Where(s => s.TaskStatus == TaskStatusValue.Warning);

                TraceMsgL(TraceLevel.Verbose, "Status of the triggered tasks: ");
                TraceMsgL(TraceLevel.Verbose, String.Format("Completed:{0}  Failed:{1}  Warning:{2}  Aborted:{3}", completed.Count(), failed.Count(), warning.Count(), aborted.Count()));
            }

            return allTasksSucceded;
        }

        static void MonitorTriggeredTasks(List<TaskExecutionItem> executionItems)
        {
            Trace.Indent();

            if (executionItems.Count == 0) return;

            List<TaskExecutionItem> triggeredItems = new List<TaskExecutionItem>();

            executionItems.ForEach(item =>
            {
                TraceMsgL(TraceLevel.Verbose, "Checking triggered task:");
                EDXStatus singleResult = PollSingleTask(item.ExecId, _pollIntervall, _timeout);
                if (singleResult != null)
                {
                    _statuse.Add(singleResult);
                    triggeredItems.AddRange(singleResult.TriggeredTasksId);
                }
            });

            if (triggeredItems.Count > 0)
                MonitorTriggeredTasks(triggeredItems);

            Trace.Unindent();
        }

        static EDXStatus PollSingleTask(Guid execId, int pollInterval, int timeout)
        {
            EDXStatus result = null;
            bool writeTaskDetails = true;
            int retries = 3;
            double d = 45000 / pollInterval;
            int retriesForWait = Math.Max(Convert.ToInt32(Math.Ceiling(d)), 2);

            bool completedBeforeTimeout = SpinWait.SpinUntil(() =>
            {
                do
                {
                    try
                    {
                        result = _client.GetEDXTaskStatus(_qdsid, execId);
                        if (writeTaskDetails)
                        {
                            TraceMsg(TraceLevel.Verbose, "Checking the status of task ");
                            if (!(result == null)) TraceMsg(TraceLevel.Verbose, String.Format("\"{0}\" ", result.TaskName));
                            writeTaskDetails = false;
                        }

                        break;
                    }
                    catch (System.Exception ex)
                    {
                        retries--;
                        TraceMsgL(TraceLevel.Verbose, ".");

                        if (result == null)
                            TraceMsgL(TraceLevel.Error, String.Format("Error while checking the status of task with execId={0}, retries={1}", execId, retries));
                        else
                            TraceMsgL(TraceLevel.Error, String.Format("Error while checking the status of task {0} (id={1} execId={2}), retries={3}", result.TaskName, result.TaskId, result.ExecId, retries));

                        TraceMsgL(TraceLevel.Error, String.Format("Error message: {0}", ex.Message));

                        if (retries == 0)
                        {
                            TraceMsgL(TraceLevel.Warning, "Unable to get task status.");

                            result = null;
                            // exit from 'completedBeforeTimeout' with 'true', not 'false'
                            return true;
                        }
                        else
                        {
                            Thread.Sleep(10000);
                        }
                    }
                } while (retries > 0);


                if (result != null)
                {
                    switch (result.TaskStatus)
                    {
                        case TaskStatusValue.Completed:
                        case TaskStatusValue.Warning:
                            TraceMsgL(TraceLevel.Verbose, ".");
                            return true;
                        case TaskStatusValue.Failed:
                            TraceMsgL(TraceLevel.Verbose, " failed! ");
                            return true;
                        case TaskStatusValue.Aborting:
                            TraceMsgL(TraceLevel.Verbose, " aborted! ");
                            return true;
                        case TaskStatusValue.Disabled:
                            TraceMsg(TraceLevel.Verbose, " disabled! ");
                            return true;
                        case TaskStatusValue.Unrunnable:
                            TraceMsg(TraceLevel.Verbose, " unrunnable! ");
                            return true;
                        case TaskStatusValue.Waiting:
                            if (result.TaskName.IndexOf("(work disabled)") > 0)
                            {
                                TraceMsg(TraceLevel.Verbose, " disabled! ");
                                return true;
                            }
                            retriesForWait--;
                            if (retriesForWait < 0 && result.StartTime.Length == 0)
                            {
                                TraceMsg(TraceLevel.Verbose, " disabled? ");
                                return true;
                            }
                            TraceMsg(TraceLevel.Verbose, ".");
                            Thread.Sleep(pollInterval);
                            return false;
                        default:
                            TraceMsg(TraceLevel.Verbose, ".");
                            Thread.Sleep(pollInterval);
                            return false;
                    }
                }
                else
                {
                    TraceMsgL(TraceLevel.Verbose, ".");
                    TraceMsgL(TraceLevel.Warning, "Unable to get task status.");
                    return true;
                }

            }, timeout);


            if (result != null)
            {
                if (!completedBeforeTimeout)
                    TraceMsgL(TraceLevel.Warning, "The task did not complete in time.");
                else
                {
                    TraceMsgL(TraceLevel.Info, "The task execution ended.");
                }
            }

            return result;
        }

        static bool CreateQMSAPIClient()
        {

            _client = null;
            bool ret = false;

            try
            {
                _client = new QMS5Client("BasicHttpBinding_IQMS5");
                ServiceKeyEndpointBehavior serviceKeyEndpointBehavior = new ServiceKeyEndpointBehavior();
                // client.Endpoint.Behaviors.Add(serviceKeyEndpointBehavior);
                ServiceKeyClientMessageInspector.ServiceKey = _client.GetTimeLimitedServiceKey();
                ret = true;
            }
            catch (System.Exception ex)
            {
                TraceMsgL(TraceLevel.Error, "Failed to create a client to the specified Uri.");
                TraceMsgL(TraceLevel.Error, ex.Message);
            }
            return ret;
        }

        static void GetTaskInfo(string task)
        {
            bool ok = false;
            try
            {
                List<TaskInfo> taskInfos = _client.FindEDX(task);
                foreach (TaskInfo info in taskInfos)
                {
                    TraceMsgL(TraceLevel.Info, String.Format("Task:{0}\tEnabled:{1}", info.Name, info.Enabled ? "Yes" : "No"));
                    ok = true;
                }
            }
            catch (System.Exception ex)
            {
                TraceMsgL(TraceLevel.Error, String.Format("Error message: {0}", ex.Message));
            }

            if (!ok)
                try
                {
                    TaskInfo info = _client.GetTask(new Guid(task));
                    TraceMsgL(TraceLevel.Info, String.Format("Task:{0}\tEnabled:{1}", info.Name, info.Enabled ? "Yes" : "No"));
                    ok = true;
                }
                catch (System.Exception ex)
                {
                    TraceMsgL(TraceLevel.Error, String.Format("Error message: {0}", ex.Message));
                }

            if (!ok) TraceMsgL(TraceLevel.Error, String.Format("Could not found task with id/name={0}", task));
        }

        static TriggerEDXTaskResult TriggerTask(string task, string password, string variableName, List<string> variableValues)
        {
            TriggerEDXTaskResult ret = null;
            try
            {
                //_qdsid = taskInfo.QDSID;
                ret = _client.TriggerEDXTask(_qdsid, task, password, variableName, variableValues);
            }
            catch (System.Exception ex)
            {
                TraceMsgL(TraceLevel.Error, String.Format("Error while starting task with id/name={0}", task));
                TraceMsgL(TraceLevel.Error, String.Format("Error message: {0}", ex.Message));
            }
            return ret;
        }

        static bool ListEdxTasks()
        {
            bool ret = true;
            int l, m, no, cnt = 0;
            List<TaskInfo> outList = new List<TaskInfo>();

            if (CreateQMSAPIClient())
                try
                {
                    List<ServiceInfo> qdsList = _client.GetServices(ServiceTypes.QlikViewDistributionService);
                    foreach (ServiceInfo qds in qdsList)
                    {
                        Console.WriteLine("QDS: ***** {0}  ID: {1} *****", qds.Name, qds.ID);
                        List<TaskInfo> taskInfoList2 = _client.GetTasks(qds.ID);
                        Console.Write("Searching EDX Tasks");
                        m = 0; no = 0;
                        foreach (TaskInfo info in taskInfoList2)
                        {
                            if (no % 15 == 0) Console.Write(".");
                            List<TaskInfo> taskInfoList = _client.FindEDX(info.Name);
                            foreach (TaskInfo infoEdx in taskInfoList)
                            {
                                if (infoEdx.QDSID == qds.ID)
                                {
                                    l = infoEdx.Name.Length;
                                    if (l > m) m = l;
                                    outList.Add(infoEdx);
                                    cnt++;
                                    if (cnt > 9) cnt = 0;
                                    Console.Write("{0}", cnt);
                                }
                            }
                            no++;
                        }
                        Console.WriteLine(".");
                        //Console.WriteLine("{0} Tasks found:", cnt);
                        m += 2;
                        foreach (TaskInfo info in outList)
                            Console.WriteLine("Task: {0,-" + m + "} {2,-21} {1,-10} ID: {3}", info.Name, info.Enabled ? "Enabled" : "Disabled", info.Type, info.ID);
                    }
                }
                catch (System.Exception ex)
                {
                    ret = false;
                    Console.WriteLine(String.Format("Error message: {0}", ex.Message));
                    Console.WriteLine("");
                    Console.WriteLine("!For using the -list command it's necessary to be a member in the groups \"QlikView Management API\" and \"QlikView EDX\" on the QMS server!");
                }
            //Console.ReadKey();
            return ret;
        }

        static void Usage()
        {
            Console.WriteLine("QMSEDX 12.50.0 (c) 2020 CWolf");
            Console.WriteLine("License: GPLv3 ");
            Console.WriteLine("  * Free for private and commercial use!");
            Console.WriteLine("  * No liability, no warranty!");
            Console.WriteLine("");
            Console.WriteLine("!!! This version is for QlikView 12.50 and higher !!!");
            Console.WriteLine("The program is used to execute a single EDX task and wait for the result(s).");
            Console.WriteLine("");
            Console.WriteLine("QMS Server:");
            Console.WriteLine("If QMS server is not localhost, you have to set it in file \"QMSEDX.exe.config\" in the following line:");
            Console.WriteLine("  <endpoint address=\"http://localhost:4799/QMS/Service\"... />");
            Console.WriteLine("");
            Console.WriteLine("Task list: (Membership in the groups \"QlikView Management API\" and \"QlikView EDX\" on the QMS server is necessary!)");
            Console.WriteLine("qmsedx -list");
            Console.WriteLine("  List all EDX Tasks on QDS");
            Console.WriteLine("");
            Console.WriteLine("Task execution: (Membership in the group \"QlikView EDX\" on the QMS server is necessary!)");
            Console.WriteLine("qmsedx -task=\"task name\" [-password=password] [-variablename=vname] [-variablevalues=vvalues] [-timeout=seconds] [-pollinterval=seconds] [-delay=seconds] [-nowait] [-verbosity=level]");
            Console.WriteLine("Arguments:");
            Console.WriteLine("  -task [REQUIRED]: The name or id of the task to execute.");
            Console.WriteLine("  -password (-pwd) [OPTIONAL]: The password required to execute the task, if set.");
            Console.WriteLine("  -variablename (-vn) [OPTIONAL]: The name of the variable to pass on to the task, if set.");
            Console.WriteLine("  -variablevalues (-vv) [OPTIONAL]: A semicolon separated list of values for the variable to pass on to the task.");
            Console.WriteLine("  -timeout (-to) [OPTIONAL]: How many seconds to wait for a single task to finish. Default value is 3600 (1 hours).");
            Console.WriteLine("  -pollinterval (-pi) [OPTIONAL]: How often to check the status of the task. Default value is every five seconds.");
            Console.WriteLine("  -delay [OPTIONAL]: How many seconds to delay for the program to terminate. Default is 0.");
            Console.WriteLine("  -nowait (-nw) [OPTIONAL]: Don't wait for task completion.");
            Console.WriteLine("  -verbosity (-verb) [OPTIONAL]: The level of output, 0-4. 0 will not produce any output and 4 (default) is the most verbose.\n");
            //Console.ReadKey();
        }

        static bool ParseArguments(string[] args)
        {
            bool isArgsOk = false;

            /*
            string Url = String.Format("http://{0}:4799/QMS/Service", ConfigurationManager.AppSettings["QmsHost"]);
            if (Uri.IsWellFormedUriString(Url, UriKind.Absolute))
                _qms = new Uri(Url);
            else
            {
                TraceMsgL(TraceLevel.Error, "The name for the QMS is incorrect");
                return false;
            }
            */

            foreach (string argument in args)
            {
                if (!(argument.ToLower().Equals("-list") || argument.ToLower().Equals("-nw") || argument.ToLower().Equals("-nowait") || (argument.StartsWith("-") && argument.Contains("="))))
                {
                    TraceMsgL(TraceLevel.Error, String.Format("Arguments must start with an '-' and contain '=', except '-nowait','-list'. {0} does not follow that rule.\n", argument));
                    return false;
                }

                string key;
                string value = string.Empty;

                if (argument.Contains("="))
                {
                    Match match = Regex.Match(argument, "-(?<key>.*?)=(?<value>.*)");
                    key = match.Groups["key"].Value.ToLower();
                    value = match.Groups["value"].Value;
                }
                else
                {
                    key = argument.Substring(1).ToLower();

                }

                int i;
                switch (key)
                {
                    case "timeout":
                    case "to":
                        if (int.TryParse(value, out i))
                            _timeout = i * 1000;
                        break;

                    case "delay":
                        if (int.TryParse(value, out i))
                            _delay = i * 1000;
                        break;

                    case "variablename":
                    case "vn":
                        _variableName = value;
                        break;

                    case "variablevalues":
                    case "vv":
                        _variableValues = new List<string>(value.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
                        break;

                    case "pollintervall":
                    case "pi":
                        if (int.TryParse(value, out i))
                            _pollIntervall = i * 1000;
                        break;

                    case "password":
                    case "pwd":
                        _password = value;
                        break;

                    case "task":
                        _taskIdOrName = value;
                        break;

                    case "nowait":
                    case "nw":
                        if (value == string.Empty || value.ToLower() == "true")
                            _noWait = true;
                        break;

                    case "list":
                        _list = true;
                        break;

                    case "verbosity":
                    case "verb":
                        uint verbosity;
                        if (uint.TryParse(value, out verbosity))
                        {
                            //Make sure the value is at most 4 (TraceLevel.Verbose)
                            verbosity = Math.Min((uint)TraceLevel.Verbose, verbosity);
                            _verbosity = (TraceLevel)verbosity;
                        }
                        break;

                    default:
                        TraceMsgL(TraceLevel.Warning, "Unknown argument: " + key);
                        break;
                }
            }


            if (!_list && string.IsNullOrEmpty(_taskIdOrName))
                TraceMsgL(TraceLevel.Error, "Missing argument: A task name or ID was not provided.");
            else
                isArgsOk = true;

            return isArgsOk;
        }

        static void TraceMsgL(TraceLevel level, string message)
        {
            // string dt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fffff");
            if (level > TraceLevel.Off && level <= _verbosity) Trace.WriteLine(message);
        }

        static void TraceMsg(TraceLevel level, string message)
        {
            if (level > TraceLevel.Off && level <= _verbosity) Trace.Write(message);
        }

    }
}
