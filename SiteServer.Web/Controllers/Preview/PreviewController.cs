﻿using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Web;
using System.Web.Http;
using BaiRong.Core;
using BaiRong.Core.Model;
using SiteServer.CMS.Controllers.Preview;
using SiteServer.CMS.Core;
using SiteServer.CMS.Model;
using SiteServer.CMS.Model.Enumerations;
using SiteServer.CMS.StlParser;
using SiteServer.CMS.StlParser.Model;
using SiteServer.CMS.StlParser.StlElement;
using SiteServer.CMS.StlParser.Utility;

namespace SiteServer.API.Controllers.Preview
{
    [RoutePrefix("api")]
    public class PreviewController : ApiController
    {
        private HttpResponseMessage Response(string html, PublishmentSystemInfo publishmentSystemInfo)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content =
                    new StringContent(html,
                        Encoding.GetEncoding(publishmentSystemInfo.Additional.Charset), "text/html")

            };
        }

        private HttpResponseMessage GetResponseMessage(VisualInfo visualInfo)
        {
            PublishmentSystemInfo publishmentSystemInfo =
                PublishmentSystemManager.GetPublishmentSystemInfo(visualInfo.PublishmentSystemId);
            if (publishmentSystemInfo == null) return null;

            TemplateInfo templateInfo = null;
            switch (visualInfo.TemplateType)
            {
                case ETemplateType.IndexPageTemplate:
                    templateInfo = TemplateManager.GetIndexPageTemplateInfo(visualInfo.PublishmentSystemId);
                    break;
                case ETemplateType.ChannelTemplate:
                    templateInfo = TemplateManager.GetChannelTemplateInfo(visualInfo.PublishmentSystemId, visualInfo.ChannelId);
                    break;
                case ETemplateType.ContentTemplate:
                    templateInfo = TemplateManager.GetContentTemplateInfo(visualInfo.PublishmentSystemId, visualInfo.ChannelId);
                    break;
                case ETemplateType.FileTemplate:
                    templateInfo = TemplateManager.GetFileTemplateInfo(visualInfo.PublishmentSystemId, visualInfo.FileTemplateId);
                    break;
            }

            var pageInfo = new PageInfo(visualInfo.ChannelId, visualInfo.ContentId, publishmentSystemInfo, templateInfo)
            {
                IsLocal = true
            };
            var contextInfo = new ContextInfo(pageInfo);

            var contentBuilder = new StringBuilder(TemplateManager.GetTemplateContent(publishmentSystemInfo, templateInfo));
            //需要完善，考虑单页模板、内容正文、翻页及外部链接

            if (visualInfo.TemplateType == ETemplateType.FileTemplate)           //单页
            {
                var fileContentBuilder = new StringBuilder(TemplateManager.GetTemplateContent(publishmentSystemInfo, templateInfo));
                Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);
                return Response(fileContentBuilder.ToString(), publishmentSystemInfo);
            }
            if (visualInfo.TemplateType == ETemplateType.IndexPageTemplate || visualInfo.TemplateType == ETemplateType.ChannelTemplate)        //栏目页面
            {
                var nodeInfo = NodeManager.GetNodeInfo(visualInfo.PublishmentSystemId, visualInfo.ChannelId);
                if (nodeInfo == null) return null;

                if (nodeInfo.ParentId > 0)
                {
                    if (!string.IsNullOrEmpty(nodeInfo.LinkUrl))
                    {
                        PageUtils.Redirect(nodeInfo.LinkUrl);
                        return null;
                    }
                }

                var stlLabelList = StlParserUtility.GetStlLabelList(contentBuilder.ToString());

                //如果标签中存在Content
                var stlContentElement = string.Empty;

                foreach (var label in stlLabelList)
                {
                    if (StlParserUtility.IsStlChannelElement(label, NodeAttribute.PageContent))
                    {
                        stlContentElement = label;
                        break;
                    }
                }

                if (!string.IsNullOrEmpty(stlContentElement))//内容存在
                {
                    var innerBuilder = new StringBuilder(stlContentElement);
                    StlParserManager.ParseInnerContent(innerBuilder, pageInfo, contextInfo);
                    var contentAttributeHtml = innerBuilder.ToString();
                    var pageCount = StringUtils.GetCount(ContentUtility.PagePlaceHolder, contentAttributeHtml) + 1;//一共需要的页数
                    if (pageCount > 1)
                    {
                        Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                        for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId,
                                pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var index = contentAttributeHtml.IndexOf(ContentUtility.PagePlaceHolder, StringComparison.Ordinal);
                            var length = index == -1 ? contentAttributeHtml.Length : index;

                            if (currentPageIndex == visualInfo.PageIndex)
                            {
                                var pagedContentAttributeHtml = contentAttributeHtml.Substring(0, length);
                                var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlContentElement, pagedContentAttributeHtml));
                                StlParserManager.ReplacePageElementsInChannelPage(pagedBuilder, thePageInfo, stlLabelList, thePageInfo.PageNodeId, currentPageIndex, pageCount, 0);

                                return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                            }

                            if (index != -1)
                            {
                                contentAttributeHtml = contentAttributeHtml.Substring(length + ContentUtility.PagePlaceHolder.Length);
                            }
                        }
                        return null;
                    }
                    contentBuilder.Replace(stlContentElement, contentAttributeHtml);
                }

                if (StlParserUtility.IsStlElementExists(StlPageContents.ElementName, stlLabelList))//如果标签中存在<stl:pageContents>
                {
                    var stlElement = StlParserUtility.GetStlElement(StlPageContents.ElementName, stlLabelList);
                    var stlPageContentsElement = stlElement;
                    var stlPageContentsElementReplaceString = stlElement;

                    var pageContentsElementParser = new StlPageContents(stlPageContentsElement, pageInfo, contextInfo, false);
                    int totalNum;
                    var pageCount = pageContentsElementParser.GetPageCount(out totalNum);

                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId,
                                pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var pageHtml = pageContentsElementParser.Parse(totalNum, currentPageIndex, pageCount, false);
                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlPageContentsElementReplaceString, pageHtml));

                            StlParserManager.ReplacePageElementsInChannelPage(pagedBuilder, thePageInfo, stlLabelList, thePageInfo.PageNodeId, currentPageIndex, pageCount, totalNum);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }
                    }
                }
                else if (StlParserUtility.IsStlElementExists(StlPageChannels.ElementName, stlLabelList))//如果标签中存在<stl:pageChannels>
                {
                    var stlElement = StlParserUtility.GetStlElement(StlPageChannels.ElementName, stlLabelList);
                    var stlPageChannelsElement = stlElement;
                    var stlPageChannelsElementReplaceString = stlElement;

                    var pageChannelsElementParser = new StlPageChannels(stlPageChannelsElement, pageInfo, contextInfo, false);
                    int totalNum;
                    var pageCount = pageChannelsElementParser.GetPageCount(out totalNum);

                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId, pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var pageHtml = pageChannelsElementParser.Parse(currentPageIndex, pageCount);
                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlPageChannelsElementReplaceString, pageHtml));

                            StlParserManager.ReplacePageElementsInChannelPage(pagedBuilder, thePageInfo, stlLabelList, thePageInfo.PageNodeId, currentPageIndex, pageCount, totalNum);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }
                    }
                }
                else if (StlParserUtility.IsStlElementExists(StlPageSqlContents.ElementName, stlLabelList))//如果标签中存在<stl:pageSqlContents>
                {
                    var stlElement = StlParserUtility.GetStlElement(StlPageSqlContents.ElementName, stlLabelList);
                    var stlPageSqlContentsElement = stlElement;
                    var stlPageSqlContentsElementReplaceString = stlElement;

                    var pageSqlContentsElementParser = new StlPageSqlContents(stlPageSqlContentsElement, pageInfo, contextInfo, false);
                    int totalNum;
                    var pageCount = pageSqlContentsElementParser.GetPageCount(out totalNum);

                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId, pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var pageHtml = pageSqlContentsElementParser.Parse(currentPageIndex, pageCount);
                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlPageSqlContentsElementReplaceString, pageHtml));

                            StlParserManager.ReplacePageElementsInChannelPage(pagedBuilder, thePageInfo, stlLabelList, thePageInfo.PageNodeId, currentPageIndex, pageCount, totalNum);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }
                    }
                }

                Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);
                return Response(contentBuilder.ToString(), publishmentSystemInfo);
            }
            if (visualInfo.TemplateType == ETemplateType.ContentTemplate)        //内容页面
            {
                if (contextInfo.ContentInfo == null) return null;

                if (!string.IsNullOrEmpty(contextInfo.ContentInfo.GetString(ContentAttribute.LinkUrl)))
                {
                    PageUtils.Redirect(contextInfo.ContentInfo.GetString(ContentAttribute.LinkUrl));
                    return null;
                }

                var stlLabelList = StlParserUtility.GetStlLabelList(contentBuilder.ToString());

                if (StlParserUtility.IsStlContentElementWithTypePageContent(stlLabelList))//内容存在
                {
                    var stlElement = StlParserUtility.GetStlContentElementWithTypePageContent(stlLabelList);
                    var stlElementTranslated = StlParserManager.StlEncrypt(stlElement);
                    contentBuilder.Replace(stlElement, stlElementTranslated);

                    var innerBuilder = new StringBuilder(stlElement);
                    StlParserManager.ParseInnerContent(innerBuilder, pageInfo, contextInfo);
                    var pageContentHtml = innerBuilder.ToString();
                    var pageCount = StringUtils.GetCount(ContentUtility.PagePlaceHolder, pageContentHtml) + 1;//一共需要的页数
                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        var index = pageContentHtml.IndexOf(ContentUtility.PagePlaceHolder, StringComparison.Ordinal);
                        var length = index == -1 ? pageContentHtml.Length : index;

                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId, pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };

                            var pageHtml = pageContentHtml.Substring(0, length);

                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlElementTranslated, pageHtml));

                            StlParserManager.ReplacePageElementsInContentPage(pagedBuilder, thePageInfo, stlLabelList, visualInfo.ChannelId, visualInfo.ContentId, currentPageIndex, pageCount);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }

                        if (index != -1)
                        {
                            pageContentHtml = pageContentHtml.Substring(length + ContentUtility.PagePlaceHolder.Length);
                        }
                    }
                }

                if (StlParserUtility.IsStlElementExists(StlPageContents.ElementName, stlLabelList))//如果标签中存在<stl:pageContents>
                {
                    var stlElement = StlParserUtility.GetStlElement(StlPageContents.ElementName, stlLabelList);
                    var stlPageContentsElement = stlElement;
                    var stlPageContentsElementReplaceString = stlElement;

                    var pageContentsElementParser = new StlPageContents(stlPageContentsElement, pageInfo, contextInfo, false);
                    int totalNum;
                    var pageCount = pageContentsElementParser.GetPageCount(out totalNum);

                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId, pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var pageHtml = pageContentsElementParser.Parse(totalNum, currentPageIndex, pageCount, false);
                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlPageContentsElementReplaceString, pageHtml));

                            StlParserManager.ReplacePageElementsInContentPage(pagedBuilder, thePageInfo, stlLabelList, visualInfo.ChannelId, visualInfo.ContentId, currentPageIndex, pageCount);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }
                    }
                }
                else if (StlParserUtility.IsStlElementExists(StlPageChannels.ElementName, stlLabelList))//如果标签中存在<stl:pageChannels>
                {
                    var stlElement = StlParserUtility.GetStlElement(StlPageChannels.ElementName, stlLabelList);
                    var stlPageChannelsElement = stlElement;
                    var stlPageChannelsElementReplaceString = stlElement;

                    var pageChannelsElementParser = new StlPageChannels(stlPageChannelsElement, pageInfo, contextInfo, false);
                    int totalNum;
                    var pageCount = pageChannelsElementParser.GetPageCount(out totalNum);

                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId, pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var pageHtml = pageChannelsElementParser.Parse(currentPageIndex, pageCount);
                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlPageChannelsElementReplaceString, pageHtml));

                            StlParserManager.ReplacePageElementsInContentPage(pagedBuilder, thePageInfo, stlLabelList, visualInfo.ChannelId, visualInfo.ContentId, currentPageIndex, pageCount);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }
                    }
                }
                else if (StlParserUtility.IsStlElementExists(StlPageSqlContents.ElementName, stlLabelList))//如果标签中存在<stl:pageSqlContents>
                {
                    var stlElement = StlParserUtility.GetStlElement(StlPageSqlContents.ElementName, stlLabelList);
                    var stlPageSqlContentsElement = stlElement;
                    var stlPageSqlContentsElementReplaceString = stlElement;

                    var pageSqlContentsElementParser = new StlPageSqlContents(stlPageSqlContentsElement, pageInfo, contextInfo, false);
                    int totalNum;
                    var pageCount = pageSqlContentsElementParser.GetPageCount(out totalNum);

                    Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);

                    for (var currentPageIndex = 0; currentPageIndex < pageCount; currentPageIndex++)
                    {
                        if (currentPageIndex == visualInfo.PageIndex)
                        {
                            var thePageInfo = new PageInfo(pageInfo.PageNodeId, pageInfo.PageContentId, pageInfo.PublishmentSystemInfo, pageInfo.TemplateInfo)
                            {
                                IsLocal = true
                            };
                            var pageHtml = pageSqlContentsElementParser.Parse(currentPageIndex, pageCount);
                            var pagedBuilder = new StringBuilder(contentBuilder.ToString().Replace(stlPageSqlContentsElementReplaceString, pageHtml));

                            StlParserManager.ReplacePageElementsInContentPage(pagedBuilder, thePageInfo, stlLabelList, visualInfo.ChannelId, visualInfo.ContentId, currentPageIndex, pageCount);

                            return Response(pagedBuilder.ToString(), publishmentSystemInfo);
                        }
                    }
                }

                Parser.Parse(publishmentSystemInfo, pageInfo, contextInfo, contentBuilder, visualInfo.FilePath, true);
                StlParserManager.ReplacePageElementsInContentPage(contentBuilder, pageInfo, stlLabelList, contextInfo.ContentInfo.NodeId, contextInfo.ContentInfo.Id, 0, 1);
                return Response(contentBuilder.ToString(), publishmentSystemInfo);
            }

            return null;
        }

        [HttpGet, Route(PreviewApi.Route)]
        public HttpResponseMessage Get(int publishmentSystemId)
        {
            try
            {
                var pageIndex = TranslateUtils.ToInt(HttpContext.Current.Request.QueryString["pageIndex"]);

                var response = GetResponseMessage(VisualInfo.GetInstance(publishmentSystemId, 0, 0, 0, pageIndex, 0));
                return response ?? Request.CreateResponse(HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                LogUtils.AddSystemErrorLog(ex);
                return Request.CreateResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        [HttpGet, Route(PreviewApi.RouteChannel)]
        public HttpResponseMessage GetChannel(int publishmentSystemId, int channelId)
        {
            try
            {
                var pageIndex = TranslateUtils.ToInt(HttpContext.Current.Request.QueryString["pageIndex"]);

                var response = GetResponseMessage(VisualInfo.GetInstance(publishmentSystemId, channelId, 0, 0, pageIndex, 0));
                return response ?? Request.CreateResponse(HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                LogUtils.AddSystemErrorLog(ex);
                return Request.CreateResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        [HttpGet, Route(PreviewApi.RouteContent)]
        public HttpResponseMessage GetContent(int publishmentSystemId, int channelId, int contentId)
        {
            try
            {
                var pageIndex = TranslateUtils.ToInt(HttpContext.Current.Request.QueryString["pageIndex"]);
                var previewId = TranslateUtils.ToInt(HttpContext.Current.Request.QueryString["previewId"]);

                var response = GetResponseMessage(VisualInfo.GetInstance(publishmentSystemId, channelId, contentId, 0, pageIndex, previewId));
                return response ?? Request.CreateResponse(HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                LogUtils.AddSystemErrorLog(ex);
                return Request.CreateResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }

        [HttpGet, Route(PreviewApi.RouteFile)]
        public HttpResponseMessage GetFile(int publishmentSystemId, int fileTemplateId)
        {
            try
            {
                var pageIndex = TranslateUtils.ToInt(HttpContext.Current.Request.QueryString["pageIndex"]);

                var response = GetResponseMessage(VisualInfo.GetInstance(publishmentSystemId, 0, 0, fileTemplateId, pageIndex, 0));
                return response ?? Request.CreateResponse(HttpStatusCode.NotFound);
            }
            catch (Exception ex)
            {
                LogUtils.AddSystemErrorLog(ex);
                return Request.CreateResponse(HttpStatusCode.BadRequest, ex.Message);
            }
        }
    }
}
