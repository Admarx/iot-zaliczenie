using Opc.UaFx;
using Opc.UaFx.Client;
using System.Xml;
using OpcAgent.Device;
using Microsoft.Azure.Devices.Client;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using Microsoft.Azure.Devices;
using System.IO;

#region Startup Configuration
string filePath = null;
bool goodFile = false;
int whichExecution = 0;
Int32 telemetryDelay = 10000; // time in ms (10 seconds by default)
List<String> deviceNames = new List<string>();
List<DeviceClient> deviceClients = new List<DeviceClient>();
XmlDocument config = new XmlDocument();

string connectionAddress = string.Empty;
string deviceConnectionString = string.Empty;
string serviceBusConnectionString = string.Empty;
string emergencyStopQueueName = string.Empty;
string lowerProductionQueueName = string.Empty;
string registryManagerConnectionString = string.Empty;
string azureDeviceName = string.Empty;
bool debug = false;
#endregion 

Console.WriteLine("Reading the config file");

#region Config file
while (!goodFile)
{
    #region File Reading
    Console.WriteLine("Insert the config file path relative to the program");
    filePath = Console.ReadLine();
    if (File.Exists(filePath))
    {
        try
        {
            config.Load(filePath);
        }
        catch
        {
            config = null;
            Console.WriteLine("Incorrect config file. Make sure the file exists and has a XML structure, and then press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    else
    {
        Console.WriteLine("Config file doesn't exist under this path. Press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region File Validation

    #region Connection Address
    try
    {
        connectionAddress = config.SelectSingleNode("/DeviceConfig/ConnectionAddress").InnerXml;
        if (string.IsNullOrEmpty(connectionAddress))
        {
            Console.WriteLine("Connection address to the OPC UA server was not found in the Config file. Insert the connection string into the <ConnectionAddress> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }

    }
    catch
    {
        Console.WriteLine("Connection address to the OPC UA server was not found in the Config file. Modify the file by adding a <ConnectionAddress> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Azure Connection String
    try
    {
        deviceConnectionString = config.SelectSingleNode("/DeviceConfig/AzureConnectionString").InnerXml;
        if (string.IsNullOrEmpty(deviceConnectionString))
        {
            Console.WriteLine("Azure Device connection string was not found in the Config file. Insert the connection string into the <AzureConnectionString> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure Device connection string was not found in the Config file. Modify the file by adding a <AzureConnectionString> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Telemetry Delay
    try
    {
        string str_TelemetryDelay = config.SelectSingleNode("/DeviceConfig/TelemetryDelay").InnerXml;
        if (string.IsNullOrEmpty(str_TelemetryDelay))
        {
            Console.WriteLine("Telemetry Delay value was not found in the Config file. Default value (10 seconds) will be used instead.");
        }
        else
        {
            Int32 uint_TelemetryDelay;
            if (Int32.TryParse(str_TelemetryDelay, out uint_TelemetryDelay))
            {
                if (uint_TelemetryDelay < 5000)
                {
                    Console.WriteLine("Telemetry Delay value in the Config file was lower than 5 seconds. Default value (10 seconds) will be used instead.");
                }
                else
                {
                    telemetryDelay = uint_TelemetryDelay;
                }
            }
            else
            {
                Console.WriteLine("Telemetry Delay value was incorrect - default value (10 seconds) will be used instead.");
            }
        }
    }
    catch
    {
        Console.WriteLine("Telemetry Delay value did not exist - default value (10 seconds) will be used instead.");
    }
    #endregion

    #region Service Bus Connection String
    try
    {
        serviceBusConnectionString = config.SelectSingleNode("/DeviceConfig/ServiceBusConnectionString").InnerXml;
        if (string.IsNullOrEmpty(serviceBusConnectionString))
        {
            Console.WriteLine("Azure Service Bus connection string was not found in the Config file. Insert the connection string into the <ServiceBusConnectionString> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure Service Bus connection string was not found in the Config file. Modify the file by adding a <ServiceBusConnectionString> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Emergency Stop Queue Name
    try
    {
        emergencyStopQueueName = config.SelectSingleNode("/DeviceConfig/EmergencyStopQueueName").InnerXml;
        if (string.IsNullOrEmpty(emergencyStopQueueName))
        {
            Console.WriteLine("Azure Service Bus Queue for handling Emergency Stop was not found in the Config file. Insert the Bus Queue Name into the <EmergencyStopQueueName> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure Service Bus Queue for handling Emergency Stop was not found in the Config file. Modify the file by adding a <EmergencyStopQueueName> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Lower Production Queue Name
    try
    {
        lowerProductionQueueName = config.SelectSingleNode("/DeviceConfig/LowerProductionRateQueueName").InnerXml;
        if (string.IsNullOrEmpty(lowerProductionQueueName))
        {
            Console.WriteLine("Azure Service Bus Queue for handling Production Rate decrease was not found in the Config file. Insert the Bus Queue Name into the <LowerProductionRateQueueName> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure Service Bus Queue for handling Production Rate decrease was not found in the Config file. Modify the file by adding a <LowerProductionRateQueueName> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Azure Device Name
    try
    {
        azureDeviceName = config.SelectSingleNode("/DeviceConfig/AzureDeviceName").InnerXml;
        if (string.IsNullOrEmpty(azureDeviceName))
        {
            Console.WriteLine("Azure Device Name was not found in the Config file. Insert the Azure Device Name into the <AzureDeviceName> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure Device Name was not found in the Config file. Modify the file by adding a <AzureDeviceName> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Registry Manager Connection String
    try
    {
        registryManagerConnectionString = config.SelectSingleNode("/DeviceConfig/RegistryManagerConnectionString").InnerXml;
        if (string.IsNullOrEmpty(registryManagerConnectionString))
        {
            Console.WriteLine("Azure Registry Manager conenction string was not found in the Config file. Insert the connection string into the <RegistryManagerConnectionString> element and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure Registry Manager conenction string was not found in the Config file. Modify the file by adding a <RegistryManagerConnectionString> element and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #region Debug Mode
    try
    {
        string str_DebugMode = config.SelectSingleNode("/DeviceConfig/@debug").InnerXml;
        if (string.IsNullOrEmpty(str_DebugMode))
        {
            Console.WriteLine("Debug attribute was not found in the Config file. Default value (false) will be used instead, all information except for Direct Methods calls will be suppressed");
        }
        else
        {
            if (str_DebugMode == "true")
            {
                debug = true;
                Console.WriteLine("Debug value set to \"true\", all information will be printed out.");
            }
            else
            {
                Console.WriteLine("Debug value set to \"false\", all information except for Direct Methods calls will be suppressed");
            }
        }
    }
    catch
    {
        Console.WriteLine("Debug value did not exist - default value (false) will be used instead, all information except for Direct Methods calls will be suppressed.");
    }
    #endregion

    #region Devices
    try
    {
        var nodes = config.SelectNodes("/DeviceConfig/Device/Name");       
        if (nodes.Count == 0)
        {
            Console.WriteLine("Device information was not found in the Config file. Insert at least one <Device> element with a <Name> child node and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("There was an error with reading the Devices. Modify the file by adding at least one <Device> element with a <Name> child node and press any key to retry.");
        Console.ReadKey();
        continue;
    }
    #endregion

    #endregion

    goodFile = true;
}
#endregion

Console.WriteLine("Config file has been successfully loaded");

#region Connection
using (var client = new OpcClient(connectionAddress))
{
    client.Connect();

    #region Virtual Device Setup
    using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
    using var registryManager = RegistryManager.CreateFromConnectionString(registryManagerConnectionString);
    await deviceClient.OpenAsync();
    var device = new VirtualDevice(deviceClient, client, registryManager, azureDeviceName, debug);
    await device.InitializeHandlers();
    await device.ClearReportedTwinAsync();
    #endregion

    #region Service Bus Setup
    await using ServiceBusClient serviceBus_client = new ServiceBusClient(serviceBusConnectionString);
    await using ServiceBusProcessor emergencyStop_processor = serviceBus_client.CreateProcessor(emergencyStopQueueName);
    await using ServiceBusProcessor lowerProduction_processor = serviceBus_client.CreateProcessor(lowerProductionQueueName);

    emergencyStop_processor.ProcessMessageAsync += device.EmergencyStop_ProcessMessageAsync;
    emergencyStop_processor.ProcessErrorAsync += device.Message_ProcessError;

    lowerProduction_processor.ProcessMessageAsync += device.LowerProduction_ProcessMessageAsync;
    lowerProduction_processor.ProcessErrorAsync += device.Message_ProcessError;

    await emergencyStop_processor.StartProcessingAsync();
    await lowerProduction_processor.StartProcessingAsync();
    #endregion

    if(debug)
    {
        #region Debug
        while (true)
        {
            List<OpcReadNode[]> commandList = new List<OpcReadNode[]>();

            #region Reading Devices
            foreach (XmlNode iterator in config.SelectNodes("/DeviceConfig/Device"))
            {
                string name = iterator.SelectSingleNode("Name").InnerText;
                deviceNames.Add(name);
                if (!string.IsNullOrEmpty(name))
                {
                    OpcReadNode[] commands = new OpcReadNode[] {
        new OpcReadNode("ns=2;s="+name+"/ProductionStatus", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/ProductionStatus"),
        new OpcReadNode("ns=2;s="+name+"/ProductionRate", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/ProductionRate"),
        new OpcReadNode("ns=2;s="+name+"/WorkorderId", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/WorkorderId"),
        new OpcReadNode("ns=2;s="+name+"/Temperature", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/Temperature"),
        new OpcReadNode("ns=2;s="+name+"/GoodCount", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/GoodCount"),
        new OpcReadNode("ns=2;s="+name+"/BadCount", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/BadCount"),
        new OpcReadNode("ns=2;s="+name+"/DeviceError", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/DeviceError"),
        };

                    commandList.Add(commands);
                }
            }
            #endregion

            #region Printing
            await device.PrintTwinAsync();
            whichExecution = 0;
            foreach (OpcReadNode[] command in commandList)
            {
                IEnumerable<OpcValue> job = client.ReadNodes(command);
                Console.WriteLine("Printing information about: " + deviceNames[whichExecution]);
                whichExecution++;

                if (job.All(x => x.Value == null))
                {
                    Console.WriteLine("No information found about this device. Please check the configuration file and/or the device itself.");
                }
                else
                {
                    foreach (var item in job)
                    {
                        var testvalue = item.Value;
                        Console.WriteLine(item.Value);
                    }
                }
                Console.Write("\n");
            }
            #endregion

            #region Sending
            whichExecution = 0;
            foreach (OpcReadNode[] command in commandList)
            {
                IEnumerable<OpcValue> job = client.ReadNodes(command);
                List<String> itemValues = new List<string>();

                if (!job.All(x => x.Value == null))
                {
                    foreach (var item in job)
                    {
                        itemValues.Add(item.Value.ToString());
                    }
                }
                await device.UpdateReportedTwinAsync(deviceNames[whichExecution], itemValues);
                await device.UpdateProductionRate(deviceNames[whichExecution]);
                whichExecution++;
            }
            await Task.Delay(telemetryDelay);
            #endregion
        }
        #endregion
    }
    else
    {
        #region OPC Agent
        while (true)
        {
            List<OpcReadNode[]> commandList = new List<OpcReadNode[]>();

            #region Reading Devices
            foreach (XmlNode iterator in config.SelectNodes("/DeviceConfig/Device"))
            {
                string name = iterator.SelectSingleNode("Name").InnerText;
                deviceNames.Add(name);
                if (!string.IsNullOrEmpty(name))
                {
                    OpcReadNode[] commands = new OpcReadNode[] {
        new OpcReadNode("ns=2;s="+name+"/ProductionStatus", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/ProductionStatus"),
        new OpcReadNode("ns=2;s="+name+"/ProductionRate", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/ProductionRate"),
        new OpcReadNode("ns=2;s="+name+"/WorkorderId", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/WorkorderId"),
        new OpcReadNode("ns=2;s="+name+"/Temperature", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/Temperature"),
        new OpcReadNode("ns=2;s="+name+"/GoodCount", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/GoodCount"),
        new OpcReadNode("ns=2;s="+name+"/BadCount", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/BadCount"),
        new OpcReadNode("ns=2;s="+name+"/DeviceError", OpcAttribute.DisplayName),
        new OpcReadNode("ns=2;s="+name+"/DeviceError"),
        };

                    commandList.Add(commands);
                }
            }
            #endregion

            #region Sending
            whichExecution = 0;
            foreach (OpcReadNode[] command in commandList)
            {
                IEnumerable<OpcValue> job = client.ReadNodes(command);
                List<String> itemValues = new List<string>();

                if (!job.All(x => x.Value == null))
                {
                    foreach (var item in job)
                    {
                        itemValues.Add(item.Value.ToString());
                    }
                }
                await device.UpdateReportedTwinAsync(deviceNames[whichExecution], itemValues);
                await device.UpdateProductionRate(deviceNames[whichExecution]);
                whichExecution++;
            }
            await Task.Delay(telemetryDelay);
            #endregion
        }
        #endregion
    }

}
#endregion
