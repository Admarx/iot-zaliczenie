using Opc.UaFx;
using Opc.UaFx.Client;
using System.Xml;
using OpcAgent.Device;
using Microsoft.Azure.Devices.Client;
using Azure.Messaging.ServiceBus;
using Newtonsoft.Json;
using Microsoft.Azure.Devices;

#region startup configs
string filePath = Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory()).Parent.FullName).ToString() + "\\DeviceConfig.xml";
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
#endregion 

Console.WriteLine("Reading the config file");

#region reading config file
while (!goodFile)
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

    try
    {
        connectionAddress = config.SelectSingleNode("/DeviceConfig/ConnectionAddress").InnerXml;
        if(string.IsNullOrEmpty(connectionAddress))
        {
            Console.WriteLine("Connection address to the OPC UA server was not found in the Config file. Modify the file and press any key to retry.");
            Console.ReadKey();
            continue;
        }    

    }
    catch
    {
        Console.WriteLine("Connection address to the OPC UA server was not found in the Config file. Modify the file and press any key to retry.");
        Console.ReadKey();
        continue;
    }

    try
    {
        deviceConnectionString = config.SelectSingleNode("/DeviceConfig/AzureConnectionString").InnerXml;
        if (string.IsNullOrEmpty(deviceConnectionString))
        {
            Console.WriteLine("Azure device connection string was not found in the Config file. Modify the file and press any key to retry.");
            Console.ReadKey();
            continue;
        }
    }
    catch
    {
        Console.WriteLine("Azure device connection string was not found in the Config file. Modify the file and press any key to retry.");
        Console.ReadKey();
        continue;
    }

    try
    {
        string str_TelemetryDelay = config.SelectSingleNode("/DeviceConfig/TelemetryDelay").InnerXml;
        if (string.IsNullOrEmpty(deviceConnectionString))
        {
            Console.WriteLine("Telemetry Delay value was not found in the Config file. Default value (10 seconds) will be used instead.");
        }
        else
        {
            Int32 uint_TelemetryDelay;
            if(Int32.TryParse(str_TelemetryDelay, out uint_TelemetryDelay))
            {
                if(uint_TelemetryDelay < 5000)
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
    // Todo: validation for 5 new fields
    serviceBusConnectionString = config.SelectSingleNode("/DeviceConfig/ServiceBusConnectionString").InnerXml;
    emergencyStopQueueName = config.SelectSingleNode("/DeviceConfig/EmergencyStopQueueName").InnerXml;
    lowerProductionQueueName = config.SelectSingleNode("/DeviceConfig/LowerProductionRateQueueName").InnerXml;
    registryManagerConnectionString = config.SelectSingleNode("/DeviceConfig/RegistryManagerConnectionString").InnerXml;
    azureDeviceName = config.SelectSingleNode("/DeviceConfig/AzureDeviceName").InnerXml;


    goodFile = true;
}
#endregion

#region connection
using (var client = new OpcClient(connectionAddress))
{
    client.Connect();

    #region before loop
    using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, Microsoft.Azure.Devices.Client.TransportType.Mqtt);
    using var registryManager = RegistryManager.CreateFromConnectionString(registryManagerConnectionString);
    await deviceClient.OpenAsync();
    var device = new VirtualDevice(deviceClient, client, registryManager, azureDeviceName);
    await device.InitializeHandlers();
    await device.ClearReportedTwinAsync();
    #endregion

    #region servicebus setup
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

    while (config != null)
    {
        
        List<OpcReadNode[]> commandList = new List<OpcReadNode[]>();

        #region reading nodes
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

        #region printing - temporary
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

        #region sending test
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
}
#endregion
