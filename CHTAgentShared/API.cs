using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RestSharp;

namespace CloudHealth
{
    public class API
    {
        public class AsyncRequestHandle
        {
            private RestRequestAsyncHandle handle;

            internal AsyncRequestHandle(RestRequestAsyncHandle handle)
            {
                this.handle = handle;
            }

            public void Abort()
            {
                handle.Abort();
            }
        }

        private RestSharp.RestClient client;
        private string accessToken;
        private Logger logger;

        public API(string baseURL, string accessToken)
        {
            logger = new Logger();
            this.accessToken = accessToken;
            client = new RestClient(baseURL);
        }

        public void ReportError(string message, string level, object details)
        {
            logger.LogInfo(String.Format("{0}: {1}", level, message));
            var request = CreateRequest("/agent/alert_agent", Method.POST);
            request.AddJsonBody(new AgentAlert(message, level, details));

            var resp = client.ExecuteAsync(request, response =>
            {
                if (response.StatusCode != System.Net.HttpStatusCode.OK && response.StatusCode != System.Net.HttpStatusCode.Created)
                {
                    logger.LogError(String.Format("Failed to send error to CloudHealth API({0}): {1}", response.StatusCode, response.Content));
                } else {
                    logger.LogError("Successfully logged agent error with CloudHealth API");
                }
            });
        }

        public AsyncRequestHandle Checkin(AgentInfo info, Action<System.Net.HttpStatusCode, AgentStatus> callback) 
        {
            logger.LogInfo("Checkin {0} for cloud {1} with key {2}", info.Instance, info.CloudName, accessToken);
            var request = CreateRequest("/agent/checkin", Method.POST);
            request.AddJsonBody(info);

            var resp = client.ExecuteAsync<AgentStatus>(request, response => {
                callback(response.StatusCode, response.Data);
            });

            return new AsyncRequestHandle(resp);
        }

        public AsyncRequestHandle RegisterAgent(AgentInfo info, Action<System.Net.HttpStatusCode, AgentStatus> callback)
        {
            logger.LogInfo("Registering {0} for cloud {1} with key {2}", info.Instance, info.CloudName, accessToken);
            var request = CreateRequest("/agent/register", Method.POST);
            request.AddJsonBody(info);

            var resp = client.ExecuteAsync<AgentStatus>(request, response =>
            {
                callback(response.StatusCode, response.Data);
            });

            return new AsyncRequestHandle(resp);
        }

        public AsyncRequestHandle UploadPerformanceData(System.IO.StreamReader jsonReader, Action<System.Net.HttpStatusCode, UploadResponse> callback) 
        {
            var request = CreateRequest("/agent/upload", Method.POST);
            string body = jsonReader.ReadToEnd();
            jsonReader.Close();
            request.AddParameter("application/json", body, ParameterType.RequestBody);
            var resp = client.ExecuteAsync<UploadResponse>(request, response =>
            {
                callback(response.StatusCode, response.Data);
            });

            return new AsyncRequestHandle(resp);
        }

        protected RestRequest CreateRequest(string path, Method method) {
            var request = new RestRequest(path, method);
            request.RequestFormat = DataFormat.Json;
            request.AddHeader("Access-Token", accessToken);
            request.JsonSerializer = new RestSharpJsonNetSerializer();
            return request;
        }

    }
}
