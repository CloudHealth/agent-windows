using RestSharp;

namespace CloudHealth
{
    class EC2InstanceMetadata
    {
        private RestClient Client { get; set; }

        public EC2InstanceMetadata()
        {
            Client = new RestClient("http://169.254.169.254");
        }

        public string InstanceId {
            get
            {
                return Client.Get(new RestRequest("/latest/meta-data/instance-id", Method.GET)).Content;
            }
        }
    }
}
