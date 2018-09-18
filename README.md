# cht_agent_windows

CloudHealth Windows Agent

## Projects

## CHTAgentService

## CHTAgentWindows

## Setup

1. Install Visual Studio Pro 2013 - Trial
2. Install InstallShield LE, which is utilized in creating the installer package
3. Install WiX Tools from http://wixtoolset.org/
4. Add "C:\Program Files (x86)\WiX Toolset v3.9\bin" to PATH
5. Open the solution w/ VS
6. Go to Tools / NuGet / Manage, and you should have an option to Restore packages. This will install the Rest and JSON libraries.
7. Switch to "Release" in the toolbar (instead of "Debug")
8. Manually build the two CHT projects (not Setup yet)
9. Fix CHTAgent setup path within the "Setup" project under "Setup / Specify Application Data / Redistributables" (sort by Name to find CHTAgent). Right click, go to "Browse for Merge Module..." and go to the "bin/Release" folder and select "ServiceInstaller.msm" and now you can build the Setup project.
8. Setup package will be created by VS in cht_agent_windows\Setup\Setup\Express\SingleImage\DiskImages\DISK1

### Hints for issues w/ Setup

Set VirtualBox adapter to Bridge mode to communicate with sandbox
sudo iptables -A INPUT -p tcp --dport 9292 -j ACCEPT
USE_CHT_SRC=1 _JAVA_OPTIONS="-Djava.net.preferIPv4Stack=true" ./bin/rackup --host 0.0.0.0
Test from win vbox: http://192.168.30.100:9292/v1/health

## Versioning

This project *almost* follows semantic versioning. The first 3 places of
the version (e.g. 1.2.3) are treated as semantic versioning and can be
used in development to better understand magnitude of changes. The fourth
space (Revision) *MUST* be monotonically increasing as this is the number
used to identify new versions and trigger auto-update

You can increment the version of the Assemblies using:

`.\SetVersion.ps1 1.0.0.[REVISION] -path .\*`

## Registry Keys

The Agent uses two registry values for configuration. If you aren't using the installer
you may need to add these yourself:

Both values reside under the key: `HKEY_LOCAL_MACHINE\SOFTWARE\CloudHealth Technologies`

- `AgentAPIKey` - The API Key for the agent on this cloud/OS
- `CloudName` - the name of the cloud this instance is running on (e.g. aws, azure or datacenter) 

If the following registry value is specified, the connection with the server happens through a proxy server:

- `ProxyHost` - the address of the proxy server, e.g. hostname:3128

If the proxy server requires authentication, two more registry values should be specified:

- `ProxyUser` - the user name required to log on the proxy server
- `ProxyPassword` - the password required to log on the proxy server


## Installation

To test the installer or install locally you can run the silent installer:

`CloudHealthAgent.exe /S /v"/l* install.log /qn CLOUDNAME=aws CHTAPIKEY=[APIKEY]"`

where [APIKEY] is the value of the agent API key for the customer/cloud/os combination.
This value can be retrieved from mysql with a simple SQL query:

`select api_key from agent_settings where os='windows' and cloud_name='aws' and customer_id=1;`

If a proxy server should be used, up to three more options can be specified:

`CloudHealthAgent.exe /S /v"/l* install.log /qn CLOUDNAME=aws CHTAPIKEY=[APIKEY] PROXY=[hostname:port] PROXYUSER=[username] PROXYPASSWORD=[password]"`

## License

This project is licensed under the GPL v3 License.  See the LICENSE file for more details.
