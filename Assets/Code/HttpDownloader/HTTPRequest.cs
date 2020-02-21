using System.Collections;
using System.Net;
using System.IO;
/// <summary>
/// 请求消息类
/// </summary>
public static class HTTPRequest 
{
    /// <summary>
    /// 根据参数获取返回数据
    /// </summary>
    /// <param name="param"></param>
    /// <returns></returns>
    public static (FileStream,Stream,long,long) Get(HTTPParamIndie param)
    {
        if (param == null)
            return GetDefaultReturn();

        // 构建文件流
        FileStream fs       = new FileStream(param.Path, FileMode.OpenOrCreate, FileAccess.Write);
        // 文件当前长度
        long fileLength     = fs.Length;
        long TotalLength    = 0;
        //设置当前文件数据位置
        fs.Seek(fileLength, SeekOrigin.Begin);

        // 发送请求开始下载
        HttpWebRequest request  = WebRequest.Create(param.Url) as HttpWebRequest;
        if (request == null)
            return GetDefaultReturn();

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

    /// <summary>
    /// 返回默认参数
    /// </summary>
    /// <returns></returns>
    private static (FileStream, Stream, long, long) GetDefaultReturn()
    {
        return (null, null, 0, 0);
    }
}
