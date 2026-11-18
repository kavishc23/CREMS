using System.Net;
using CREMS.Api.Data;
using CREMS.Api.Domain.Customers;
using CREMS.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using SkiaSharp;
using Xunit;
namespace CREMS.Api.Tests;
public sealed class CustomerLicenceStorageBehaviorTests
{
    private static CancellationToken T=>TestContext.Current.CancellationToken;
    internal sealed class EnvironmentFixture:IWebHostEnvironment,IDisposable
    {
        public string ContentRootPath{get;set;}=Path.Combine(Path.GetTempPath(),"crems-licence-test-"+Guid.NewGuid().ToString("N"));public string WebRootPath{get;set;}=Path.GetTempPath();public string ApplicationName{get;set;}="CREMS.Api.Tests";public string EnvironmentName{get;set;}="Testing";public IFileProvider ContentRootFileProvider{get;set;}=new NullFileProvider();public IFileProvider WebRootFileProvider{get;set;}=new NullFileProvider();public void Dispose(){if(Directory.Exists(ContentRootPath))Directory.Delete(ContentRootPath,true);}
    }
    private sealed class Ocr(string text="Name: TRAINING CUSTOMER\nLicence Number: 1234567\nClasses: 2",Exception? failure=null):ILicenceOcrProvider
    {public Task<string> ReadFirstPageAsync(byte[] content,string contentType,CancellationToken token)=>failure is null?Task.FromResult(text):Task.FromException<string>(failure);}
    internal static IFormFile Image()
    {using var bitmap=new SKBitmap(800,500);using var canvas=new SKCanvas(bitmap);canvas.Clear(SKColors.White);using var paint=new SKPaint{Color=SKColors.Black};for(var x=0;x<800;x+=20)for(var y=0;y<500;y+=20)if((x+y)%40==0)canvas.DrawRect(x,y,10,10,paint);using var bytes=bitmap.Encode(SKEncodedImageFormat.Png,100);var stream=new MemoryStream(bytes.ToArray());return new FormFile(stream,0,stream.Length,"file","training.png"){Headers=new HeaderDictionary(),ContentType="image/png"};}
    private static async Task<Customer> Customer(ApplicationDbContext db){var c=new Customer{CustomerNumber="LICENCE-TRAIN",Name="Training Customer"};db.Customers.Add(c);await db.SaveChangesAsync(T);return c;}
    [Fact]public async Task Manual_verification_replaces_previous_record_and_removal_deletes_only_stored_fixture_files()
    {await using var db=MaintenanceJobsTests.Database();using var env=new EnvironmentFixture();var c=await Customer(db);var service=new CustomerLicenceService(db,new Ocr(),env);var first=await service.SaveManualAsync(c.Id,Image()," 1234567 ",[2,1,2],LicenceUploadSource.Customer,null,T);Assert.Equal("1234567",first.LicenceNumber);Assert.Equal("1,2",first.LicenceClasses);Assert.Equal(LicenceVerificationStatus.Verified,first.Status);var firstPath=service.ResolvePath(first);Assert.True(File.Exists(firstPath));var second=await service.SaveManualAsync(c.Id,Image(),"7654321",[2],LicenceUploadSource.Customer,null,T);Assert.Equal(LicenceVerificationStatus.Required,first.Status);Assert.Equal(second.Id,(await service.CurrentAsync(c.Id,T))!.Id);Assert.Equal("7654321",c.IdentificationNumber);var secondPath=service.ResolvePath(second);await service.RemoveAsync(c.Id,T);Assert.Null(await service.CurrentAsync(c.Id,T));Assert.Null(c.IdentificationNumber);Assert.False(File.Exists(firstPath));Assert.False(File.Exists(secondPath));}
    [Fact]public async Task Scan_stages_extracted_fields_and_confirmation_requires_unchanged_matching_details()
    {await using var db=MaintenanceJobsTests.Database();using var env=new EnvironmentFixture();var c=await Customer(db);var service=new CustomerLicenceService(db,new Ocr(),env);var staged=await service.ScanAsync(c.Id,Image(),LicenceUploadSource.Customer,null,T);Assert.True(staged.NameMatches);Assert.Equal("1234567",staged.LicenceNumber);var changed=await Assert.ThrowsAsync<LicenceException>(()=>service.ConfirmAsync(c.Id,staged.SubmissionId,"9999999",[2],null,true,T));Assert.Equal("scan_details_changed",changed.Code);var confirmed=await service.ConfirmAsync(c.Id,staged.SubmissionId,"1234567",[2],null,true,T);Assert.Equal(LicenceVerificationStatus.Verified,confirmed.Status);}
    [Theory][InlineData(true)][InlineData(false)]public async Task Failed_OCR_is_recorded_and_never_marks_a_licence_verified(bool known)
    {await using var db=MaintenanceJobsTests.Database();using var env=new EnvironmentFixture();var c=await Customer(db);var service=new CustomerLicenceService(db,new Ocr(failure:known?new LicenceException("ocr_no_text","No text"):new IOException("Provider failed")),env);var error=await Assert.ThrowsAsync<LicenceException>(()=>service.ScanAsync(c.Id,Image(),LicenceUploadSource.Customer,null,T));Assert.Equal(known?"ocr_no_text":"ocr_service_failed",error.Code);var record=Assert.Single(db.CustomerLicences);Assert.Equal(LicenceOcrStatus.Failed,record.OcrStatus);Assert.Equal(error.Code,record.OcrFailureCode);Assert.Null(await service.CurrentAsync(c.Id,T));}
    [Theory][InlineData("")][InlineData("not an image")]
    public async Task Empty_and_unsupported_files_are_rejected_before_storage(string content)
    {await using var db=MaintenanceJobsTests.Database();using var env=new EnvironmentFixture();var c=await Customer(db);var bytes=System.Text.Encoding.UTF8.GetBytes(content);var file=new FormFile(new MemoryStream(bytes),0,bytes.Length,"file","unsafe.png");var service=new CustomerLicenceService(db,new Ocr(),env);var error=await Assert.ThrowsAsync<LicenceException>(()=>service.SaveManualAsync(c.Id,file,"1234567",[2],LicenceUploadSource.Customer,null,T));Assert.Equal(content.Length==0?"file_size":"unsupported_file",error.Code);Assert.Empty(db.CustomerLicences);Assert.False(Directory.Exists(env.ContentRootPath));}
    [Fact]public async Task Storage_resolution_rejects_path_traversal_and_missing_files()
    {await using var db=MaintenanceJobsTests.Database();using var env=new EnvironmentFixture();var service=new CustomerLicenceService(db,new Ocr(),env);Assert.Throws<FileNotFoundException>(()=>service.ResolvePath(new(){ContentHash="test",ContentType="image/png",StorageKey="../../secret.txt"}));Assert.Throws<FileNotFoundException>(()=>service.ResolvePath(new(){ContentHash="test",ContentType="image/png",StorageKey="missing.png"}));}
    [Fact]public async Task Unknown_customers_and_submissions_report_domain_errors()
    {await using var db=MaintenanceJobsTests.Database();using var env=new EnvironmentFixture();var service=new CustomerLicenceService(db,new Ocr(),env);Assert.Equal("customer_not_found",(await Assert.ThrowsAsync<LicenceException>(()=>service.ScanAsync(Guid.NewGuid(),Image(),LicenceUploadSource.Customer,null,T))).Code);var c=await Customer(db);Assert.Equal("submission_not_found",(await Assert.ThrowsAsync<LicenceException>(()=>service.ConfirmAsync(c.Id,Guid.NewGuid(),"1234567",[2],null,true,T))).Code);Assert.Equal("customer_not_found",(await Assert.ThrowsAsync<LicenceException>(()=>service.RemoveAsync(Guid.NewGuid(),T))).Code);}
    private sealed class Handler(HttpStatusCode status,string body):HttpMessageHandler
    {public string? Authorization;public string? Payload;protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token){Authorization=request.Headers.Authorization?.ToString();Payload=await request.Content!.ReadAsStringAsync(token);return new(status){Content=new StringContent(body)};}}
    private sealed class ClientFactory(HttpClient client):IHttpClientFactory{public HttpClient CreateClient(string name)=>client;}
    [Fact]public async Task Remote_OCR_posts_only_first_page_with_optional_authentication_and_returns_text()
    {var handler=new Handler(HttpStatusCode.OK,"{\"text\":\"Training licence text\"}");using var client=new HttpClient(handler);var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"LicenceOcr:Endpoint","https://ocr.example.test/read"},{"LicenceOcr:ApiKey","test-key"}}).Build();var provider=new HttpLicenceOcrProvider(new ClientFactory(client),config);Assert.Equal("Training licence text",await provider.ReadFirstPageAsync([1,2,3],"application/pdf",T));Assert.Equal("Bearer test-key",handler.Authorization);Assert.Contains("licence.pdf",handler.Payload);Assert.Contains("name=page",handler.Payload);}
    [Theory][InlineData(500,"{}","ocr_service_failed")][InlineData(200,"{}","ocr_no_text")][InlineData(200,"{\"text\":\"\"}","ocr_no_text")]
    public async Task Remote_OCR_failures_are_actionable_domain_errors(int status,string body,string code)
    {using var client=new HttpClient(new Handler((HttpStatusCode)status,body));var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>{{"LicenceOcr:Endpoint","https://ocr.example.test/read"}}).Build();var provider=new HttpLicenceOcrProvider(new ClientFactory(client),config);Assert.Equal(code,(await Assert.ThrowsAsync<LicenceException>(()=>provider.ReadFirstPageAsync([1,2,3],"image/png",T))).Code);}
}
