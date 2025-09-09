using System;

namespace XIVSync.API.Routes;

public class MareFiles
{
	public const string Cache = "/cache";

	public const string Cache_Get = "get";

	public const string Request = "/request";

	public const string Request_Cancel = "cancel";

	public const string Request_Check = "check";

	public const string Request_Enqueue = "enqueue";

	public const string Request_RequestFile = "file";

	public const string ServerFiles = "/files";

	public const string ServerFiles_DeleteAll = "deleteAll";

	public const string ServerFiles_FilesSend = "filesSend";

	public const string ServerFiles_GetSizes = "getFileSizes";

	public const string ServerFiles_Upload = "upload";

	public const string ServerFiles_UploadMunged = "uploadMunged";

	public const string ServerFiles_DownloadServers = "downloadServers";

	public const string Distribution = "/dist";

	public const string Distribution_Get = "get";

	public const string Main = "/main";

	public const string Main_SendReady = "sendReady";

	public const string Speedtest = "/speedtest";

	public const string Speedtest_Run = "run";

	public static Uri CacheGetFullPath(Uri baseUri, Guid requestId)
	{
		return new Uri(baseUri, "/cache/get?requestId=" + requestId);
	}

	public static Uri RequestCancelFullPath(Uri baseUri, Guid guid)
	{
		return new Uri(baseUri, "/request/cancel?requestId=" + guid);
	}

	public static Uri RequestCheckQueueFullPath(Uri baseUri, Guid guid)
	{
		return new Uri(baseUri, "/request/check?requestId=" + guid);
	}

	public static Uri RequestEnqueueFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/request/enqueue");
	}

	public static Uri RequestRequestFileFullPath(Uri baseUri, string hash)
	{
		return new Uri(baseUri, "/request/file?file=" + hash);
	}

	public static Uri ServerFilesDeleteAllFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/files/deleteAll");
	}

	public static Uri ServerFilesFilesSendFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/files/filesSend");
	}

	public static Uri ServerFilesGetSizesFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/files/getFileSizes");
	}

	public static Uri ServerFilesUploadFullPath(Uri baseUri, string hash)
	{
		return new Uri(baseUri, "/files/upload/" + hash);
	}

	public static Uri ServerFilesUploadMunged(Uri baseUri, string hash)
	{
		return new Uri(baseUri, "/files/uploadMunged/" + hash);
	}

	public static Uri ServerFilesGetDownloadServersFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/files/downloadServers");
	}

	public static Uri DistributionGetFullPath(Uri baseUri, string hash)
	{
		return new Uri(baseUri, "/dist/get?file=" + hash);
	}

	public static Uri SpeedtestRunFullPath(Uri baseUri)
	{
		return new Uri(baseUri, "/speedtest/run");
	}

	public static Uri MainSendReadyFullPath(Uri baseUri, string uid, Guid request)
	{
		return new Uri(baseUri, "/main/sendReady/?uid=" + uid + "&requestId=" + request);
	}
}
