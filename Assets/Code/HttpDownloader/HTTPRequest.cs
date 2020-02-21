using UnityEngine;
using System.Collections;
using System.Net;
using System.IO;

public static class HTTPRequest 
{
    public static (FileStream,Stream,long,long) Get(HTTPParamIndie param)
    {
        if (param == null)
        {
            Debug.Log("[HTTPRequest.Get] param is null");
            return (null,null,0,0);
        }

        // 构建文件流
        FileStream fs   = new FileStream(param.Path, FileMode.OpenOrCreate, FileAccess.Write);
        // 文件当前长度
        long fileLength = fs.Length;
        long TotalLength = 0;
        //设置当前文件数据位置
        fs.Seek(fileLength, SeekOrigin.Begin);

        // 发送请求开始下载
        HttpWebRequest request  = WebRequest.Create(param.Url) as HttpWebRequest;
        request.Timeout         = param.TimeOut;

        WebResponse response    = request.GetResponse();
        if(response != null)
        {
            //获取文件总大小
            TotalLength = response.ContentLength;
            if (fileLength >= TotalLength)
                return (null,null,fileLength,TotalLength);
        }
        request.AddRange(fs.Length);
        response = request.GetResponse();

        // 读取文件内容
        Stream stream           = response.GetResponseStream();
        if (stream.CanTimeout)
        {
            stream.ReadTimeout  = param.TimeOut;
        }
        return (fs,stream,fileLength,TotalLength);
    }
}
