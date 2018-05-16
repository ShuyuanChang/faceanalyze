#r "Microsoft.WindowsAzure.Storage"
#r "Newtonsoft.Json"
#r "System.Configuration"
#r "System.Data"

using System;
using System.IO;
using System.Web;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net;
using System.Text;
using Microsoft.WindowsAzure.Storage; // Namespace for CloudStorageAccount
using Microsoft.WindowsAzure.Storage.Blob; // Namespace for Blob storage types
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Microsoft.WindowsAzure.Storage.Queue;
using System.Configuration;
using System.Data.SqlClient;
using System.Data;
using System.Threading.Tasks;

public async static Task Run(CloudBlockBlob myBlob, string name, TraceWriter log)
{
    log.Info($"C# Blob trigger function Processed blob\n Name:{name} \n");

    await MakeAnalysisRequest(myBlob, name, log);

    log.Info("Async Done.");
} // end of Run.

static async Task MakeAnalysisRequest(CloudBlockBlob myBlob, string name, TraceWriter log)
{
    //[TODO] Modify Face API Subscription Information.
    string subscriptionKey = "[Face API Sunscription Key]";
    //[TODO] Modify region & return attributes information
    string uriBase = "https://[Your Region].api.cognitive.microsoft.com/face/v1.0/detect?returnFaceId=true&returnFaceLandmarks=false&returnFaceAttributes=age,gender,emotion";


    HttpClient client = new HttpClient();
    // Request headers.
    client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", subscriptionKey);

    // Assemble the URI for the REST API Call.
    //string uri = uriBase + "?" + requestParameters;
    string uri = uriBase; 
    HttpResponseMessage response;  
    string faceid = "";
    string happy = "0";
    float age = 0;
    string gender = "";

    //log.Info("Start...");
    //Read the frame
    using (var memoryStream = new MemoryStream())
    {
        myBlob.DownloadToStream(memoryStream);
        memoryStream.Seek(0, SeekOrigin.Begin);
        byte[] byteData = memoryStream.ToArray();

        using (ByteArrayContent content = new ByteArrayContent(byteData))
        {
            // This example uses content type "application/octet-stream".
            // The other content types you can use are "application/json" and "multipart/form-data".
            content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            //log.Info("before:");
            // Execute the REST API call.
            response = await client.PostAsync(uri, content);
            //log.Info("After:");
            // Get the JSON response.
            string contentString = await response.Content.ReadAsStringAsync();
            log.Info(contentString);
            JArray faces = JArray.Parse(contentString);
            foreach (JObject face in faces.Children<JObject>())
            {
                //log.Info(face.ToString());
                if (face["faceId"] != null){
                    faceid = face["faceId"].ToString();
                    happy = face["faceAttributes"]["emotion"]["happiness"].ToString();
                    age = float.Parse(face["faceAttributes"]["age"].ToString());
                    gender = face["faceAttributes"]["gender"].ToString();

                    log.Info("faceid = " + face["faceId"]);
                    log.Info("gender = " + face["faceAttributes"]["gender"]);
                    log.Info("age = " + face["faceAttributes"]["age"]);
                    log.Info("happy = " + face["faceAttributes"]["emotion"]["happiness"]);

                    //Find a similar face from Large Face List
                    uri = "https://westus.api.cognitive.microsoft.com/face/v1.0/findsimilars";
                    //Modify if you would like to return more similar faces
                    byteData = Encoding.UTF8.GetBytes("{\"faceId\":\"" + faceid + "\","
                        + "\"largeFaceListId\":\"dlinkfaces\",\"maxNumOfCandidatesReturned\":1}");

                    using (var contentFaceId = new ByteArrayContent(byteData))
                    {
                        contentFaceId.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                        response = await client.PostAsync(uri, contentFaceId);
                        contentString = await response.Content.ReadAsStringAsync();
                        bool bAddface = false;

                        if (response.StatusCode == HttpStatusCode.OK){
                            faces = JArray.Parse(contentString);
                            //[TODO] Apply confidence value.

                            if(faces.Count > 0){
                                log.Info("The face exists." + contentString);
                                UpdateFaceInfo(faces[0]["persistedFaceId"].ToString(),float.Parse(happy),age,gender,log);    
                            }
                            else{ bAddface = true; }
                        }
                        else if (response.StatusCode == HttpStatusCode.BadRequest){
                            //contentString = await response.Content.ReadAsStringAsync();
                            dynamic jsonData = JObject.Parse(contentString);
                            
                            if (jsonData["error"]["code"].ToString()=="FaceNotFound"){
                                bAddface = true;
                            }else{
                                //Error
                                log.Info("Find Similar Bad Request:" + contentString);
                            }
                        }
                        else{
                            log.Info("Find Similar Error! "+ contentString);
                        }

                        if (bAddface){
                                log.Info("Add Face");
                                memoryStream.Seek(0, SeekOrigin.Begin);
                                byteData = memoryStream.ToArray();
                                using (ByteArrayContent contentAdd = new ByteArrayContent(byteData))
                                {   
                                    var faceRectangle = face["faceRectangle"];
                                    uri = "https://westus.api.cognitive.microsoft.com/face/v1.0/largefacelists/dlinkfaces/persistedfaces?"
                                        +"targetFace="+faceRectangle["left"]+","+faceRectangle["top"]+","+faceRectangle["width"]+","+faceRectangle["height"];
                                    contentAdd.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                                    response = await client.PostAsync(uri, contentAdd);
                                    contentString = await response.Content.ReadAsStringAsync();
                                    log.Info(contentString);
                                    var jsonData = JObject.Parse(contentString);

                                    UpdateFaceInfo(jsonData["persistedFaceId"].ToString(),float.Parse(happy),age,gender,log);
                                    log.Info("Face added.");
                                    
                                    //[TODO] Check Training Status before call Train.
                                    uri = "https://westus.api.cognitive.microsoft.com/face/v1.0/largefacelists/dlinkfaces/train";
                                    response = await client.PostAsync(uri, null);
                                    log.Info("Train status:" + response.StatusCode);
                                }
                        } // end of Add Face
                    } // end of contentFaceId
                }
            } // end of foreach
        } // end of querying content
    } // end of memorystream

} // end of MakeAnalysisRequest

//Update face infornmation to SQL Databace
public static async Task UpdateFaceInfo(string faceId, float happy, float age, string gender, TraceWriter log)
{
    var str = ConfigurationManager.ConnectionStrings["FaceDB"].ConnectionString;
    using (SqlConnection conn = new SqlConnection(str))
    {
        conn.Open();
        log.Info("happy:"+happy);
        using (SqlCommand cmd = new SqlCommand("AddFace", conn))
        {
            // Execute the command and log the # rows affected.
            // [TODO] Exception handling
            cmd.CommandType = CommandType.StoredProcedure;
            cmd.Parameters.Add(
		            new SqlParameter("@faceId", faceId));
            cmd.Parameters.Add(
		            new SqlParameter("@happy", happy));
            cmd.Parameters.Add(
		            new SqlParameter("@age", age));
            cmd.Parameters.Add(
		            new SqlParameter("@gender", gender));
            var rows = await cmd.ExecuteNonQueryAsync();
            log.Info($"{rows} rows were updated");
            log.Info("test");
        }
    }
}