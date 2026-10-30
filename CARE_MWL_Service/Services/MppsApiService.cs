using System;
using System.Configuration;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Plexus_MWL_Service.logs;

namespace Worklist_SCP.Services
{
    public class MppsApiService
    {
        private readonly HttpClient _httpClient;

        public MppsApiService()
        {
            _httpClient = new HttpClient();

            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            // If your API requires authentication:
            // _httpClient.DefaultRequestHeaders.Add(
            //     "Authorization",
            //     "Bearer YOUR_TOKEN");
        }
        public static Serilog.ILogger fileLogger = null;
        public async Task<bool> UpdateMppsStatusAsync(
            string procedureStepId,
            string mppsSopInstanceUid,
            string status,string accessionNumber)
        {
            ucls_ReadWriteLog objReadWriteLog = new ucls_ReadWriteLog();
            try
            {
                
                objReadWriteLog.WriteToLog("Update Mpps status : accession number : " + accessionNumber, true);
                //fileLogger.Information($"MPPS Status :  {status}");

                var requestData = new
                {
                    procedureStepId = procedureStepId,
                    mppsSopInstanceUid = mppsSopInstanceUid,
                    status = status,
                    accessionNumber = accessionNumber
                };
                objReadWriteLog.WriteToLog("1",true);

                string baseUrl = ConfigurationManager.AppSettings["careBaseUrl"].ToString();
                objReadWriteLog.WriteToLog("2", true);
                string token = ConfigurationManager.AppSettings["careToken"].ToString();
                objReadWriteLog.WriteToLog("3", true);
                string json = JsonConvert.SerializeObject(requestData);
                objReadWriteLog.WriteToLog("4", true);
                string requestUrl = baseUrl +
                    "/api/care_radiology/webhooks/mpps/";


                //using (var content = new StringContent(
                //    json,
                //    Encoding.UTF8,
                //    "application/json"))
                //{
                //    //var response = await _httpClient.PostAsync(
                //    //    "https://YOUR-API-URL/api/mpps/status",
                //    //    content);

                //    //string responseBody =
                //    //    await response.Content.ReadAsStringAsync();

                //    //if (response.IsSuccessStatusCode)
                //    //{
                //    //    return true;
                //    //}

                //    return false;
                //}
                objReadWriteLog.WriteToLog("Mpps status : URL : " + requestUrl, true);
                var content = new StringContent(
    json,
    Encoding.UTF8,
    "application/json"
);
                objReadWriteLog.WriteToLog("5", true);
                _httpClient.DefaultRequestHeaders.Remove("Authorization");
                _httpClient.DefaultRequestHeaders.Add("Authorization", token);

                var response = await _httpClient.PostAsync(
                    requestUrl,
                    content
                );

                string responseBody = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    objReadWriteLog.WriteToLog("Mpps status : URL call returns true", true);
                    return true;
                }
                objReadWriteLog.WriteToLog("Mpps status : URL call returns false", true);
                objReadWriteLog.WriteToLog("Mpps status : URL response code  : " + response.StatusCode, false);
                return false;
            }
            catch (Exception ex)
            {
                objReadWriteLog.WriteToLog("Mpps status : URL call returns ERROR : " + ex.Message, true);
                return false;
            }
        }
    }
}