using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Mime;
using System.Text;

namespace OpcAgent.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient client;
        private OpcClient opcClient;

        public VirtualDevice(DeviceClient deviceClient, OpcClient opcClient)
        {
            this.client = deviceClient;
            this.opcClient = opcClient;
        }

        #region Sending Messages

        public async Task SendMessages(List<String> readNodeValues, bool isError, string deviceName)
        {
            if (readNodeValues.Count == 14)
            {
                string dataString = string.Empty;
                if (isError)
                {
                    var data = new
                    {
                        deviceName = deviceName,
                        productionStatus = readNodeValues[1],
                        workorderID = readNodeValues[5],
                        goodCount = readNodeValues[9],
                        badCount = readNodeValues[11],
                        temperature = readNodeValues[7],
                        deviceErrors = readNodeValues[13],
                    };
                    dataString = JsonConvert.SerializeObject(data);
                }
                else
                {
                    var data = new
                    {
                        deviceName = deviceName,
                        productionStatus = readNodeValues[1],
                        workorderID = readNodeValues[5],
                        goodCount = readNodeValues[9],
                        badCount = readNodeValues[11],
                        temperature = readNodeValues[7],
                    };
                    dataString = JsonConvert.SerializeObject(data);
                }
                Message eventMessage = new Message(Encoding.UTF8.GetBytes(dataString));
                eventMessage.ContentType = MediaTypeNames.Application.Json;
                eventMessage.ContentEncoding = "utf-8";

                await client.SendEventAsync(eventMessage);
            }
        }

        #endregion Sending Messages

        #region Direct Methods

        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tUNIMPLEMENTED METHOD EXECUTED: {methodRequest.Name}");

            return new MethodResponse(0);
        }

        private async Task<MethodResponse> EmergencyStop(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { deviceName = default(string) });
            Console.WriteLine($"METHOD EXECUTED: {methodRequest.Name} FOR : {payload.deviceName}");
            opcClient.CallMethod("ns=2;s=" + payload.deviceName, "ns=2;s=" + payload.deviceName + "/EmergencyStop");

            return new MethodResponse(0);
        }

        private async Task<MethodResponse> ResetErrorStatus(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { deviceName = default(string) });
            Console.WriteLine($"METHOD EXECUTED: {methodRequest.Name} FOR : {payload.deviceName}");
            opcClient.CallMethod("ns=2;s=" + payload.deviceName, "ns=2;s=" + payload.deviceName + "/ResetErrorStatus");

            return new MethodResponse(0);
        }

        #endregion Direct Methods

        #region Device Twin

        public async Task PrintTwinAsync()
        {
            var twin = await client.GetTwinAsync();
            Console.WriteLine($"\nInitial twin value received: \n{JsonConvert.SerializeObject(twin, Formatting.Indented)}");
        }

        public async Task ClearReportedTwinAsync()
        {
            var twin = await client.GetTwinAsync();
            var reportedProperties = new TwinCollection();
            string reportedJSON = twin.Properties.Reported.ToJson(Formatting.None);
            Dictionary<string, object> propertiesDict = JsonConvert.DeserializeObject<Dictionary<string, object>>(reportedJSON);
            propertiesDict.Remove("$version");
            foreach (var value in propertiesDict)
            {
                reportedProperties[value.Key] = null;
            }

            try
            {
                await client.UpdateReportedPropertiesAsync(reportedProperties);
            }
            catch
            {
                Console.WriteLine("Reported Device Twin cleanup failed.");
            }
        }

        public async Task UpdateReportedTwinAsync(string deviceName, List<string> errorValues)
        {
            if (errorValues.Count == 14)
            {
                #region variables
                bool sendDeviceError = false; // our flag checking if there was a device Error change

                var twin = await client.GetTwinAsync();
                string json = JsonConvert.SerializeObject(twin, Formatting.Indented);
                JObject jobjectJSON = JObject.Parse(json);

                string error_PropertyName = deviceName.Replace(" ", "") + "_error_state";
                string error_previousValue = (string)jobjectJSON["properties"]["reported"][error_PropertyName];
                string rate_PropertyName = deviceName.Replace(" ", "") + "_production_rate";
                var reportedProperties = new TwinCollection();
                #endregion

                #region D2C message

                if (error_previousValue != null && error_previousValue != errorValues[13]) // If there was a device error change (but it didn't appear for the first time - send to IoT)
                {
                    sendDeviceError = true;
                }

                reportedProperties[error_PropertyName] = errorValues[13];
                reportedProperties[rate_PropertyName] = errorValues[3];
                await SendMessages(errorValues, sendDeviceError, deviceName);
                await client.UpdateReportedPropertiesAsync(reportedProperties);
                #endregion
            }
        }
        public async Task UpdateProductionRate(string deviceName)
        {
            var twin = await client.GetTwinAsync();
            string json = JsonConvert.SerializeObject(twin, Formatting.Indented);
            JObject jobjectJSON = JObject.Parse(json);

            string desired_productionRateName = deviceName.Replace(" ", "") + "_production_rate";
            string desired_productionRateValue = (string)jobjectJSON["properties"]["desired"][desired_productionRateName];

            if (!string.IsNullOrEmpty(desired_productionRateValue))
            {
                int int_ProductionRate;
                if (int.TryParse(desired_productionRateValue, out int_ProductionRate))
                {
                    opcClient.WriteNode("ns=2;s=" + deviceName + "/ProductionRate", int_ProductionRate);
                }
            }
        }

        #endregion Device Twin

        public async Task InitializeHandlers()
        {
            await client.SetMethodHandlerAsync("EmergencyStop", EmergencyStop, client);
            await client.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatus, client);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, client);
        }
    }
}
