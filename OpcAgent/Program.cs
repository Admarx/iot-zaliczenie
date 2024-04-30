using Opc.UaFx;
using Opc.UaFx.Client;
using System.Xml;

#region configs
string filePath = Directory.GetParent(Directory.GetParent(Directory.GetCurrentDirectory()).Parent.FullName).ToString() + "\\DeviceConfig.xml";
int whichExecution = 0;
List<String> deviceNames = new List<string>();
XmlDocument config = new XmlDocument();
try
{
    config.Load(filePath);
}
catch
{
    config = null;
}

#endregion 

#region connection
using (var client = new OpcClient("opc.tcp://localhost:4840/"))
{
    client.Connect();
    List<OpcReadNode[]> commandList = new List<OpcReadNode[]>();

    if (config != null)
    {
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
    }
}
#endregion