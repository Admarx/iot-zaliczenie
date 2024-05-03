using Opc.UaFx;
using Opc.UaFx.Client;
using Microsoft.Azure.Devices.Client;
using Microsoft.Azure.Devices.Shared;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Net.Mime;
using System.Text;
using System.Xml.Linq;
using DotNetty.Common.Utilities;

namespace OpcAgent.Device
{
    public class VirtualDevice
    {
        private readonly DeviceClient client;

        public VirtualDevice(DeviceClient deviceClient)
        {
            this.client = deviceClient;
        }

        #region Sending Messages

        public async Task SendMessages(List<String> readNodeValues, bool isError)
        {
            if (readNodeValues.Count == 14)
            {
                string dataString = string.Empty;
                if (isError)
                {
                    var data = new
                    {
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

                //eventMessage.Properties.Add("deviceErrors", readNodeValues[13]);

                //Console.WriteLine($"\t{DateTime.Now.ToLocalTime()}> Sending message: {count}, Data: [{dataString}]");
                await client.SendEventAsync(eventMessage);

            }
            //await Task.Delay(delay);
        }

        #endregion Sending Messages

        #region Receiving Messages

        private async Task OnC2dMessageReceivedAsync(Message receivedMessage, object _)
        {
            Console.WriteLine($"\t{DateTime.Now}> C2D message callback - message received with Id={receivedMessage.MessageId}.");
            PrintMessage(receivedMessage);

            await client.CompleteAsync(receivedMessage);
            Console.WriteLine($"\t{DateTime.Now}> Completed C2D message with Id={receivedMessage.MessageId}.");

            receivedMessage.Dispose();
        }

        private void PrintMessage(Message receivedMessage)
        {
            string messageData = Encoding.ASCII.GetString(receivedMessage.GetBytes());
            Console.WriteLine($"\t\tReceived message: {messageData}");

            int propCount = 0;
            foreach (var prop in receivedMessage.Properties)
            {
                Console.WriteLine($"\t\tProperty[{propCount++}> Key={prop.Key} : Value={prop.Value}");
            }
        }

        #endregion Receiving Messages

        #region Direct Methods

        //private async Task<MethodResponse> SendMessagesHandler(MethodRequest methodRequest, object userContext)
        //{
        //    Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");

        //    var payload = JsonConvert.DeserializeAnonymousType(methodRequest.DataAsJson, new { nrOfMessages = default(int), delay = default(int) });

        //    await SendMessages(payload.nrOfMessages, payload.delay);

        //    return new MethodResponse(0);
        //}

        private static async Task<MethodResponse> DefaultServiceHandler(MethodRequest methodRequest, object userContext)
        {
            Console.WriteLine($"\tMETHOD EXECUTED: {methodRequest.Name}");

            await Task.Delay(1000);

            return new MethodResponse(0);
        }

        #endregion Direct Methods

        #region Device Twin

        public async Task UpdateTwinAsync()
        {
            var twin = await client.GetTwinAsync();
            Console.WriteLine($"\nInitial twin value received: \n{JsonConvert.SerializeObject(twin, Formatting.Indented)}");
            Console.WriteLine();

            var reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastAppLaunch"] = DateTime.Now;

            await client.UpdateReportedPropertiesAsync(reportedProperties);
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
                await SendMessages(errorValues, sendDeviceError);
                await client.UpdateReportedPropertiesAsync(reportedProperties);
                #endregion
            }
        }

        private async Task OnDesiredPropertyChanged(TwinCollection desiredProperties, object userContext)
        {
            Console.WriteLine($"\tDesired property change:\n\t{JsonConvert.SerializeObject(desiredProperties)}");
            Console.WriteLine("\tSending current time as reported property");
            TwinCollection reportedProperties = new TwinCollection();
            reportedProperties["DateTimeLastDesiredPropertyChangeReceived"] = DateTime.Now;

            await client.UpdateReportedPropertiesAsync(reportedProperties).ConfigureAwait(false);
        }
        public async Task UpdateProductionRate(OpcClient opcClient, string deviceName)
        {
            var twin = await client.GetTwinAsync();
            string json = JsonConvert.SerializeObject(twin, Formatting.Indented);
            JObject jobjectJSON = JObject.Parse(json);

            string desired_productionRateName = deviceName.Replace(" ", "") + "_production_rate";
            string desired_productionRateValue = (string)jobjectJSON["properties"]["desired"][desired_productionRateName];

            if (!string.IsNullOrEmpty(desired_productionRateValue))
            {
                int int_ProductionRate; 
                if(int.TryParse(desired_productionRateValue, out int_ProductionRate))
                {
                    opcClient.WriteNode("ns=2;s=" + deviceName + "/ProductionRate", int_ProductionRate);
                }
            }
        }

        #endregion Device Twin

        public async Task InitializeHandlers()
        {
            await client.SetReceiveMessageHandlerAsync(OnC2dMessageReceivedAsync, client);

            //await client.SetMethodHandlerAsync("SendMessages", SendMessagesHandler, client);
            await client.SetMethodDefaultHandlerAsync(DefaultServiceHandler, client);

            await client.SetDesiredPropertyUpdateCallbackAsync(OnDesiredPropertyChanged, client);
        }


    }
}
