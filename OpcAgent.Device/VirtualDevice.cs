using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using System.Net.Mime;
using System.Text;
using Azure.Messaging.ServiceBus;
using Microsoft.Azure.Devices;
using Message = Microsoft.Azure.Devices.Client.Message;

namespace OpcAgent.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient client;
        private OpcClient opcClient;
        private RegistryManager registryManager;
        private string azureDeviceName;
        bool debug; // Determines whether we should print information to the console or not

        public VirtualDevice(DeviceClient deviceClient, OpcClient opcClient, RegistryManager registryManager, string azureDeviceName, bool debug)
        {
            this.client = deviceClient;
            this.opcClient = opcClient;
            this.registryManager = registryManager;
            this.azureDeviceName = azureDeviceName;
            this.debug = debug;
        }

        #region Sending Messages

        public async Task SendMessages(List<String> readNodeValues, bool isError, string deviceName)
        {
            string dataString = string.Empty;
            if (isError) // if a DeviceError occured, we need to report it to the IoT Hub via the D2C message
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

        #endregion Sending Messages

        #region Direct Methods
        // Default Task for handling "Stray Direct Methods"
        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tUnimplemented method executed: {methodRequest.Name}");

            return new MethodResponse(0);
        }
        // Task for handling Emergency Stop Direct Method
        private async Task<MethodResponse> EmergencyStop(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { deviceName = default(string) });
            Console.WriteLine($"Emergency Stop executed for: {payload.deviceName}");
            opcClient.CallMethod("ns=2;s=" + payload.deviceName, "ns=2;s=" + payload.deviceName + "/EmergencyStop");

            return new MethodResponse(0);
        }
        // Task for handling Reset Error Status Direct Method
        private async Task<MethodResponse> ResetErrorStatus(MethodRequest methodRequest, object userContext)
        {
            var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { deviceName = default(string) });
            Console.WriteLine($"Reset Error Status executed for: {payload.deviceName}");
            opcClient.CallMethod("ns=2;s=" + payload.deviceName, "ns=2;s=" + payload.deviceName + "/ResetErrorStatus");

            return new MethodResponse(0);
        }

        #endregion Direct Methods

        #region Device Twin
        // Method for printing the Device Twin information
        public async Task PrintTwinAsync()
        {
            var twin = await client.GetTwinAsync();
            Console.WriteLine($"\nInitial twin value received: \n{JsonConvert.SerializeObject(twin, Formatting.Indented)}");
        }

        // Method for clearing the Reported Device Twin
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
                if (!debug) { Console.WriteLine("Reported Device Twin cleanup failed."); }
            }
        }

        // Method for updating the Reported Device Twin with current DeviceErrors and ProductionRate values
        public async Task UpdateReportedTwinAsync(string deviceName, List<string> errorValues)
        {
            #region variables
            bool sendDeviceError = false; // our flag checking if there was a device Error change

            var twin = await client.GetTwinAsync();
            string error_previousValue = null;

            string error_PropertyName = deviceName.Replace(" ", "") + "_error_state";
            try
            {
                error_previousValue = twin.Properties.Reported[error_PropertyName];
            }
            catch (ArgumentOutOfRangeException) { } // If value doesn't exist - do nothing
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
            // Since we have information whether or not a Device Error change happened, we can pass it to SendMessages method to decide whether to send Device Errors to IoT Hub
            await SendMessages(errorValues, sendDeviceError, deviceName);             
            await client.UpdateReportedPropertiesAsync(reportedProperties);
            #endregion
        }
        // Method for updating the Production Rate on the Server (using Desired Device Twin)
        public async Task UpdateProductionRate(string deviceName)
        {
            var twin = await client.GetTwinAsync();

            string desired_productionRateName = deviceName.Replace(" ", "") + "_production_rate";
            string desired_productionRateValue = null;

            try
            {
                desired_productionRateValue = twin.Properties.Desired[desired_productionRateName];
            }
            catch (ArgumentOutOfRangeException) { } // If value doesn't exist - do nothing

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

        #region Business Logic
        // We need to create a MethodRequest, to call EmergencyStop Direct Method
        public async Task EmergencyStop_ProcessMessageAsync(ProcessMessageEventArgs arg)
        {
            string deviceName = arg.Message.MessageId;
            string str_data = "{\"deviceName\":\"" + deviceName + "\"}";
            byte[] byte_data = Encoding.ASCII.GetBytes(str_data);
            MethodRequest methodRequest = new MethodRequest(JsonConvert.SerializeObject(str_data), byte_data);
            await EmergencyStop(methodRequest, client);

            await arg.CompleteMessageAsync(arg.Message);
        }

        public async Task LowerProduction_ProcessMessageAsync(ProcessMessageEventArgs arg)
        {
            string deviceName = arg.Message.MessageId;
            var twin = await registryManager.GetTwinAsync(azureDeviceName);
            string rate_PropertyName = deviceName.Replace(" ", "") + "_production_rate";
            string rate_previousValue = null;

            try
            {
                rate_previousValue = twin.Properties.Reported[rate_PropertyName];
            }
            catch (ArgumentOutOfRangeException) { } // If value doesn't exist - do nothing
            if (!string.IsNullOrEmpty(rate_previousValue))
            {
                int int_previousRate;
                if (int.TryParse(rate_previousValue, out int_previousRate)) // We lower the value by 10 percentage points - minimum value is 0
                {
                    if (int_previousRate - 10 > 0)
                    {
                        int_previousRate -= 10;
                    }
                    else
                    {
                        int_previousRate = 0;
                    }
                    twin.Properties.Desired[rate_PropertyName] = int_previousRate;
                    await registryManager.UpdateTwinAsync(twin.DeviceId, twin, twin.ETag); // We only update the Device Twin, during the next "telemetry Cycle" the value will readjust
                    Console.WriteLine($"Production Rate lowered for: {deviceName} due to KPI below 90%");
                }
            }
            await arg.CompleteMessageAsync(arg.Message);
        }
        // Default method for handling message errors from Service Bus Queues
        public Task Message_ProcessError(ProcessErrorEventArgs arg)
        {
            if (!debug) { Console.WriteLine("Service Bus encountered an error. Please see the attached message: " + arg.Exception.Message); }
            else { Console.WriteLine("Service Bus encountered an error. Please see the attached message: " + arg.Exception.ToString()); }
            return Task.CompletedTask;
        }
        #endregion Business Logic
        // Handler initializer, allowing us to connect Methods to DirectMethod aliases and set a Default Method Handler
        public async Task InitializeHandlers()
        {
            await client.SetMethodHandlerAsync("EmergencyStop", EmergencyStop, client);
            await client.SetMethodHandlerAsync("ResetErrorStatus", ResetErrorStatus, client);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, client);
        }

    }
}
