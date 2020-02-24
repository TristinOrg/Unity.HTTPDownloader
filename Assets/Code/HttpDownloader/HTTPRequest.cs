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
        long totalLength    = 0;
        //设置当前文件数据位置
        fs.Seek(fileLength, SeekOrigin.Begin);

        // 发送请求开始下载
        HttpWebRequest request  = WebRequest.Create(param.Url) as HttpWebRequest;
        if (request == null)
            return GetDefaultReturn();

        request.Timeout         = param.TimeOut;

        WebResponse response    = request.GetResponse();
        if (response == null)
            return GetDefaultReturn();

         //获取文件总大小
         totalLength = response.ContentLength;       
         if (fileLength >= totalLength)
            return (null,null,fileLength, totalLength);

        request.AddRange(fs.Length);
        response = request.GetResponse();
        if (response == null)
            return GetDefaultReturn();

        // 读取文件内容
        Stream stream           = response.GetResponseStream();
        if (stream == null)
            return GetDefaultReturn();

        if (stream.CanTimeout)
        {
            stream.ReadTimeout  = param.TimeOut;
        }
        return (fs,stream,fileLength, totalLength);
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
