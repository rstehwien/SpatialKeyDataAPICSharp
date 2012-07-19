using System;
using System.Xml;
using System.Web;
using System.Net;
using System.IO;
using System.Globalization;
using System.Text;
using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;

namespace SpatialKeyDataAPICSharp
{
	public class Uploader
	{
		public delegate void Logger(string message);

		public string organizationName { get; private set; }

		public string userName { get; private set; }

		public string password { get; private set; }

		public string cluster { get; private set; }

		public Cookie jsessionID { get; private set; }

		public Logger logger { get; set; }

		public Uploader (string organizationName = null, string userName = null, string password = null, Logger logger = null)
		{
			Init (organizationName, userName, password, logger);
		}

		public void Init (string organizationName = null, string userName = null, string password = null, Logger logger = null)
		{
			this.organizationName = organizationName;
			this.userName = userName;
			this.password = password;
			this.logger = logger;

			cluster = null;
			jsessionID = null; 
		}

		private void Log(string message)
		{
			if (logger != null) logger(message);
		}

		private void ClusterLookup ()
		{
			string url = String.Format ("http://{0}.spatialkey.com/clusterlookup.cfm", organizationName);
			Log(String.Format("ClusterLookup: {0}", url));
			
			XmlDocument doc = new XmlDocument ();
			doc.Load (url);
			Log(doc.InnerXml);
			
			XmlNode node = doc.SelectSingleNode ("/organization/cluster");
			cluster = node.InnerText;

			Log(String.Format("Cluster: {0}", cluster));
		}
		
		private void Authenticate ()
		{
			if (cluster == null) {
				ClusterLookup ();
			}

			string password = HttpUtility.UrlEncode(this.password);
			string url = String.Format ("https://{0}/SpatialKeyFramework/dataImportAPI?action=login&orgName={1}&user={2}&password={3}", 
			                           cluster, organizationName, HttpUtility.UrlEncode (userName), password);
			Log(String.Format("Authenticate: {0}", url.Replace(password, "XXX")));

			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create (url);
			request.Method = "GET";
			CookieContainer cookieJar = new CookieContainer ();
			request.CookieContainer = cookieJar;

			using (HttpWebResponse response = (HttpWebResponse)request.GetResponse ())
			using (StreamReader streamReader = new StreamReader(response.GetResponseStream()))
			{
				if (response.StatusCode != HttpStatusCode.OK) {
					throw new SystemException ("Authentication Failed");
				}
		
				jsessionID = cookieJar.GetCookies (request.RequestUri) ["JSESSIONID"];
				Log(streamReader.ReadToEnd());
			}

			Log(jsessionID.ToString());
		}

		#region Upload
		public void UploadData (string csvPath, string xmlPath, string action = "overwrite", bool runAsBackground = true, bool notifyByEmail = false, bool addAllUsers = false)
		{
			Log(String.Format("UploadData: {0} {1}", csvPath, xmlPath));

			string zipPath = ZipData (new string[] {csvPath, xmlPath});

			try
			{
				UploadZip (zipPath, action, runAsBackground, notifyByEmail, addAllUsers);
			}
			finally
			{
				File.Delete (zipPath);
			}

			Log("UploadData: Complete");
		}

		public void UploadZip (string zipPath, string action = "overwrite", bool runAsBackground = true, bool notifyByEmail = false, bool addAllUsers = false)
		{
			Authenticate ();

			Log(String.Format("UploadZip: {0}", zipPath));

			/*
			string protocol = "https://";
			string host = cluster;
			string path = "/SpatialKeyFramework/dataImportAPI";
			*/
			/* */
			string protocol = "http://";
			string host = "devinternal.spatialkey.com";
			string path = "/dump.cfm";
			/* */

			string url = String.Format ("{0}{1}{2}?action={3}&runAsBackground={4}&notifyByEmail={5}&addAllUsers={6}", 
			                            protocol,
			                            host,
			                            path,
			                            action, 
			                            runAsBackground.ToString().ToLower(), 
			                            notifyByEmail.ToString().ToLower(), 
			                            addAllUsers.ToString().ToLower());


			// create the jsessionID cookie
			CookieContainer cookieJar = new CookieContainer ();
			Cookie cookie = new Cookie(jsessionID.Name, jsessionID.Value);
			cookieJar.Add(new Uri(String.Format("{0}{1}", protocol, host)), cookie);

			HttpWebRequest request;
			using (FileStream zipStream = File.Open(zipPath, FileMode.Open))
			{
				UploadFile file = new UploadFile{
	                Name = "file",
	                Filename = Path.GetFileName(zipPath),
	                ContentType = "application/octet-stream",
	                Stream = zipStream
				};

				request = CreateUploadFileRequest(url, cookieJar, file);
			}

			try
			{
				using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
				using (StreamReader streamReader = new StreamReader(response.GetResponseStream()))
				{
					if (response.StatusCode != HttpStatusCode.OK) {
						throw new SystemException ("Authentication Failed");
					}
					Log(streamReader.ReadToEnd());
				}
			} 
			catch(Exception e) {
				if (e is WebException)
				{
					WebResponse errResp = ((WebException)e).Response;
					using(StreamReader streamReader = new StreamReader(errResp.GetResponseStream()))
					{
						Log(streamReader.ReadToEnd());
					}
				}
				else
				{
					throw e;
				}
			}
			Log("UploadZip: Complete");
		}

		private HttpWebRequest CreateUploadFileRequest(string url, CookieContainer cookieJar, UploadFile file)
		{
			Log(String.Format("CreateUploadFileRequest: {0} {1}", url, file.Filename));

			HttpWebRequest request = (HttpWebRequest)HttpWebRequest.Create (url);
			request.CookieContainer = cookieJar;

			request.Method = "POST";
			var boundary = "---------------------------" + DateTime.Now.Ticks.ToString("x", NumberFormatInfo.InvariantInfo);
			request.ContentType = "multipart/form-data; boundary=" + boundary;
			boundary = "--" + boundary;

			using (var requestStream = request.GetRequestStream())
			{
				var buffer = Encoding.ASCII.GetBytes(boundary + Environment.NewLine);
                requestStream.Write(buffer, 0, buffer.Length);

                buffer = Encoding.UTF8.GetBytes(string.Format("Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\"{2}", file.Name, file.Filename, Environment.NewLine));
                requestStream.Write(buffer, 0, buffer.Length);

                buffer = Encoding.ASCII.GetBytes(string.Format("Content-Type: {0}{1}{1}", file.ContentType, Environment.NewLine));
                requestStream.Write(buffer, 0, buffer.Length);

                file.Stream.CopyTo(requestStream);

                buffer = Encoding.ASCII.GetBytes(Environment.NewLine);
                requestStream.Write(buffer, 0, buffer.Length);

				var boundaryBuffer = Encoding.ASCII.GetBytes(boundary + "--");
				requestStream.Write(boundaryBuffer, 0, boundaryBuffer.Length);

				requestStream.Flush();
   				requestStream.Close();
			}

			Log("CreateUploadFileRequest: Complete");
			return request;
		}
		#endregion

		#region Zip Up Files

		private string ZipData (string[] paths)
		{
			Log(String.Format("ZipData: {0}", String.Join(", ", paths)));

			string zipPath = GetTempFile ("zip");
			ZipOutputStream zipStream = new ZipOutputStream (File.Create (zipPath));

			foreach (string path in paths)
			{
				ZipAdd(zipStream, path);
			}

			zipStream.Finish ();
			zipStream.IsStreamOwner = true;	// Makes the Close also Close the underlying stream
			zipStream.Close ();
    		
			Log(String.Format("ZipData: {0}", zipPath));
			return zipPath;
		}

		private void ZipAdd (ZipOutputStream zipStream, string fName)
		{
			Log(String.Format("ZipAdd: {0}", fName));
			FileInfo fi = new FileInfo (fName);

			// add the entry
			ZipEntry newEntry = new ZipEntry (fi.Name);
			newEntry.DateTime = fi.LastWriteTime;
			// To permit the zip to be unpacked by built-in extractor in WinXP and Server2003, WinZip 8, Java, and other older code,
			// you need to do one of the following: Specify UseZip64.Off, or set the Size.
			// If the file may be bigger than 4GB, or you do not need WinXP built-in compatibility, you do not need either,
			// but the zip will be in Zip64 format which not all utilities can understand.
			//   zipStream.UseZip64 = UseZip64.Off;
			newEntry.Size = fi.Length;

			zipStream.PutNextEntry (newEntry);

			// Zip the file in buffered chunks
			// the "using" will close the stream even if an exception occurs
			using (FileStream streamReader = File.OpenRead(fName)) {
				streamReader.CopyTo(zipStream);
			}
			zipStream.CloseEntry ();
			Log("ZipAdd: Complete");
		}

		private string GetTempFile (string fileExtension)
		{
			string temp = System.IO.Path.GetTempPath ();
			string res = string.Empty;
			while (true) {
				res = string.Format ("{0}.{1}", Guid.NewGuid ().ToString (), fileExtension);
				res = System.IO.Path.Combine (temp, res);
				if (!System.IO.File.Exists (res)) {
					try {
						System.IO.FileStream s = System.IO.File.Create (res);
						s.Close ();
						break;
					} catch (Exception) {

					}
				}
			}
			return res;
		}

		#endregion

		private class UploadFile
		{
			public UploadFile()
			{
				ContentType = "application/octet-stream";
			}
			public string Name { get; set; }
			public string Filename { get; set; }
			public string ContentType { get; set; }
			public Stream Stream { get; set; }
		}
		
	}
}

