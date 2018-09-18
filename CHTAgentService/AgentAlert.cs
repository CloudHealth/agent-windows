using Newtonsoft.Json;

namespace CloudHealth
{
    class AgentAlert
    {
        [JsonProperty("instance")]
        public string Identifier;

        [JsonProperty("alert_type")]
        public string AlertType;

        [JsonProperty("cloud_name")]
        public string CloudName;

        [JsonProperty("alert_message")]
        public string Message;

        [JsonProperty("alert_content")]
        public object Content;

        [JsonProperty("os")]
        public string OS;

        public AgentAlert(string message, string type, object content)
        {
            var agentInfo = AgentInfo.GetAgentInfo();
            Identifier = agentInfo.Identifier;
            AlertType = type;
            CloudName = agentInfo.CloudName;
            Message = message;
            Content = content;
            OS = agentInfo.OS;
        }
    }
}
