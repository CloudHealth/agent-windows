using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RestSharp;

namespace CloudHealth
{
    class EC2InstanceMetadata
    {
        private RestSharp.RestClient client { get; set; }

        public EC2InstanceMetadata()
        {
            client = new RestSharp.RestClient("http://169.254.169.254");
        }

        public string InstanceId {
            get
            {
                return client.Get(new RestSharp.RestRequest("/latest/meta-data/instance-id", Method.GET)).Content;
            }
        }
    }
}
