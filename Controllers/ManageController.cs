﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using Microsoft.Azure.Management.WebSites;
using Microsoft.WindowsAzure;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ARMOAuth.Controllers
{
    public class ManageController : ApiController
    {
        [Authorize]
        public HttpResponseMessage GetMethods(string @type)
        {
            var prop = typeof(WebSiteManagementClient).GetProperty(@type);
            var methods = prop.PropertyType.GetMethods().Where(m => m.Name.EndsWith("Async"));
            var result = new JArray();
            foreach (var method in methods)
            {
                result.Add(GetMethodDescription(method));
            }

            var response = Request.CreateResponse(HttpStatusCode.OK);
            response.Content = new StringContent(result.ToString(), Encoding.UTF8, "application/json");
            return response;
        }

        [Authorize]
        public async Task<HttpResponseMessage> GetInvokeMethod(string @type, string subscription, string method)
        {
            var token = Request.Headers.GetValues("X-MS-OAUTH-TOKEN").FirstOrDefault();
            var creds = new TokenCloudCredentials(subscription, token);

            var client = new WebSiteManagementClient(creds, new Uri("https://management.azure.com"));
            var prop = client.GetType().GetProperty(@type);
            var propValue = prop.GetValue(client);
            var func = propValue.GetType().GetMethod(method);
            var args = new List<object>();
            var query = Request.GetQueryNameValuePairs();
            foreach (var parameter in func.GetParameters())
            {
                if (parameter.ParameterType == typeof(string))
                {
                    var value = query.FirstOrDefault(q => q.Key == parameter.Name);
                    args.Add(!String.IsNullOrEmpty(value.Value) ? value.Value : null);
                }
                else if (parameter.ParameterType == typeof(CancellationToken))
                {
                    args.Add(CancellationToken.None);
                }
                else
                {
                    var serializer = new JsonSerializer();
                    using (var reader = new StreamReader(await Request.Content.ReadAsStreamAsync()))
                    {
                        args.Add(serializer.Deserialize(new JsonTextReader(reader), parameter.ParameterType));
                    }
                }
            }

            if (func.ReturnType.IsGenericType)
            {
                var result = await (dynamic)func.Invoke(propValue, args.ToArray());
                string content;
                if (result is IEnumerable)
                {
                    content = JArray.FromObject(result).ToString();
                }
                else
                {
                    content = JObject.FromObject(result).ToString();
                }
                var response = Request.CreateResponse(HttpStatusCode.OK);
                response.Content = new StringContent(content, Encoding.UTF8, "application/json");
                return response;
            }
            else
            {
                await (Task)func.Invoke(propValue, args.ToArray());
                return Request.CreateResponse(HttpStatusCode.OK);
            }
        }

        private static JObject GetMethodDescription(MethodInfo method)
        {
            var arguments = new JObject();
            foreach (var parameter in method.GetParameters())
            {
                if (parameter.ParameterType == typeof(CancellationToken))
                {
                    continue;
                }

                arguments[parameter.Name] = GetTypeDescription(parameter.ParameterType);
            }

            var description = new JObject();
            description["name"] = method.Name;
            description["arguments"] = arguments;
            return description;
        }

        private static JToken GetTypeDescription(Type type)
        {
            if (type == typeof(string))
            {
                return "string";
            }
            else if (type.IsGenericType && type.Name.StartsWith("Nullable"))
            {
                return GetTypeDescription(type.GenericTypeArguments[0]);
            }
            else if (type == typeof(int))
            {
                return "number";
            }
            else if (type == typeof(bool))
            {
                return "bool";
            }
            else if (type == typeof(DateTime))
            {
                return "datetime";
            }
            else if (type.IsEnum)
            {
                var objs = new List<object>();
                foreach (var value in type.GetEnumValues())
                {
                    objs.Add(value);
                }
                return String.Join("|", objs);
            }
            else if (type.IsArray)
            {
                var array = new JArray();
                array.Add(GetTypeDescription(type.GetElementType()));
                return array;
            }
            else if (typeof(IEnumerable).IsAssignableFrom(type))
            {
                var array = new JArray();
                array.Add(GetTypeDescription(type.GenericTypeArguments[0]));
                return array;
            }
            else
            {
                var obj = new JObject();
                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    obj[prop.Name] = GetTypeDescription(prop.PropertyType);
                }
                return obj;
            }
        }
    }
}