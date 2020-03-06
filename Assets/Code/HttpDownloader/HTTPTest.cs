using System.Collections;
using System.Collections.Generic;
using System.Timers;
using UnityEngine;
public class HTTPTest : MonoBehaviour
{
    HTTPDownloadIndie indie;
    HTTPDownloadBatch batch;
    Timer t;
    // Start is called before the first frame update
    void Start()
    {
        //steam下载地址
        string url          = @"https://media.st.dl.eccdnx.com/client/installer/steam.dmg";
        string pathFormat   = Application.dataPath + "/StreamingAssets/steam_{0}.dmg";

        //单个下载
        HTTPParamIndie paramIndie = new HTTPParamIndie(url, string.Format(pathFormat, 1), "", 1);
        indie = new HTTPDownloadIndie(paramIndie);
        indie.DownLoad();
        //----------

        ////批量下载
        List<string> urls = new List<string>();
        List<string> paths = new List<string>();
        List<string> md5s = new List<string>();
        for (int i = 2; i <= 40; i++)
        {
            urls.Add(url);
            paths.Add(string.Format(pathFormat, i));
            md5s.Add(i.ToString());
        }

        HTTPParamBatch paramBatch = new HTTPParamBatch();
        paramBatch.Urls = urls;
        paramBatch.Paths = paths;
        paramBatch.MD5S = md5s;
        paramBatch.MaxThread = 10;

        batch = new HTTPDownloadBatch(paramBatch);
        batch.DownLoad();
        //--------------

        //信息打印
        t = new Timer();
        t.Elapsed += timeCallBack;
        t.Interval = 1000;
        t.Start();
    }

    private void OnDestroy()
    {
        t.Dispose();
        HTTPTask.AbortAll();
        indie.ResetData();
        batch.ResetData();
    }
    private void timeCallBack(object source,ElapsedEventArgs args)
    {
        Debug.Log("--indie "+indie.Progress + ": progress " + indie.Status + " : status " + indie.Speed + " kbs/s " + indie.Error + " error");
        Debug.Log("--batch "+batch.Progress + ": progress " + batch.Status + " : status " + batch.Speed + " kbs/s "+batch.DownloadedCount+" :downloadedCount "+batch.Error+" error");
    }
}
