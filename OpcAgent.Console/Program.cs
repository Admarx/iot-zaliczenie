using Opc.UaFx;
using Opc.UaFx.Client;
using System.Xml;
using OpcAgent.Device;
using Microsoft.Azure.Devices.Client;

#region startup configs
string filePath = Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory()).Parent.FullName).ToString() + "\\DeviceConfig.xml";
bool goodFile = false;
int whichExecution = 0;
List<String> deviceNames = new List<string>();
List<DeviceClient> deviceClients = new List<DeviceClient>();
XmlDocument config = new XmlDocument();
string connectionAddress = string.Empty;
string deviceConnectionString = string.Empty;
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
    goodFile = true;
}
#endregion

#region connection
using (var client = new OpcClient(connectionAddress))
{
    client.Connect();

    while (config != null)
    {
        #region variables
        List<OpcReadNode[]> commandList = new List<OpcReadNode[]>();
        //string deviceConnectionString = "HostName=IOTHUBAK2024.azure-devices.net;DeviceId=AKDeviceProjekt;SharedAccessKey=nC6TZc+dPAR/X5gLyU3U4wvMv77tvGUQHQ6q832K/QE=";
        using var deviceClient = DeviceClient.CreateFromConnectionString(deviceConnectionString, TransportType.Mqtt);
        await deviceClient.OpenAsync();
        var device = new VirtualDevice(deviceClient, client);

        await device.InitializeHandlers();
        await device.UpdateTwinAsync();

        #endregion

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
        await Task.Delay(15000);
        #endregion
    }
}
#endregion
