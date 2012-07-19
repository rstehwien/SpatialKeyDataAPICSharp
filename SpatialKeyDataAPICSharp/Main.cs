using System;
using System.Xml;

namespace SpatialKeyDataAPICSharp
{
	class MainClass
	{
		public static void Main (string[] args)
		{
			string configFile = "SpatailKeyUploadConfig.xml";
			if (args.Length > 0) configFile = args[0];

			XmlDocument doc = new XmlDocument ();
			doc.Load (configFile);

			Uploader uploader = new Uploader(doc.SelectSingleNode("/config/organizationName").InnerText, 
			                                 doc.SelectSingleNode("/config/userName").InnerText, 
			                                 doc.SelectSingleNode("/config/password").InnerText, 
			                                 Log);

			uploader.UploadData(doc.SelectSingleNode("/config/csvPath").InnerText, 
			                    doc.SelectSingleNode("/config/xmlPath").InnerText,
			                    doc.SelectSingleNode("/config/action").InnerText,
			                    doc.SelectSingleNode("/config/runAsBackground").InnerText == "true",
			                    doc.SelectSingleNode("/config/notifyByEmail").InnerText == "true",
			                    doc.SelectSingleNode("/config/addAllUsers").InnerText == "true");
		}

		public static void Log(string message)
		{
			Console.WriteLine(message);
		}
	}
}
