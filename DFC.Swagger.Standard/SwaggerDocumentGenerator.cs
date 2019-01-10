﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Dynamic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using DFC.Functions.DI.Core.Attributes;
using DFC.Swagger.Standard.Annotations;
using DFC.Swagger.Standard.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Newtonsoft.Json;

namespace DFC.Swagger.Standard
{
    public static class SwaggerDocumentGenerator
    {
        public static string GenerateSwaggerDocument(HttpRequest req, string apiTitle, string apiDescription, string apiDefinitionName, Assembly assembly)
        {

            if(req == null)
                throw new ArgumentNullException(nameof(req));

            if (string.IsNullOrEmpty(apiTitle))
                throw new ArgumentNullException(nameof(apiTitle));

            if (string.IsNullOrEmpty(apiDescription))
                throw new ArgumentNullException(nameof(apiDescription));

            if (string.IsNullOrEmpty(apiDefinitionName))
                throw new ArgumentNullException(nameof(apiDefinitionName));

            if (assembly == null)
                throw new ArgumentNullException(nameof(assembly));
            
            dynamic doc = new ExpandoObject();
            doc.swagger = "2.0";
            doc.info = new ExpandoObject();
            doc.info.title = apiTitle;
            doc.info.version = "1.0.0";
            doc.info.description = apiDescription;
            doc.host = req.Host.HasValue ? req.Host.Value : string.Empty;
            doc.basePath = "/";
            doc.schemes = new[] { "https" };
            if (req.Host.ToString().Contains("127.0.0.1") || req.Host.ToString().Contains("localhost"))
            {
                doc.schemes = new[] { "http" };
            }

            doc.definitions = new ExpandoObject();
            doc.paths = GeneratePaths(assembly, doc, apiTitle, apiDefinitionName);
            doc.securityDefinitions = GenerateSecurityDefinitions();

            return JsonConvert.SerializeObject(doc);
        }

        private static dynamic GenerateSecurityDefinitions()
        {
            dynamic securityDefinitions = new ExpandoObject();
            securityDefinitions.apikeyQuery = new ExpandoObject();
            securityDefinitions.apikeyQuery.type = "apiKey";
            securityDefinitions.apikeyQuery.name = "code";
            securityDefinitions.apikeyQuery.@in = "query";
            return securityDefinitions;
        }

        private static dynamic GeneratePaths(Assembly assembly, dynamic doc, string apiTitle, string apiDefinitionName)
        {
            dynamic paths = new ExpandoObject();
            var methods = assembly.GetTypes()
                .SelectMany(t => t.GetMethods())
                .Where(m => m.GetCustomAttributes(typeof(FunctionNameAttribute), false).Length > 0).ToArray();
            foreach (MethodInfo methodInfo in methods)
            {
                //hide any disabled methods
                if (methodInfo.GetCustomAttributes(typeof(DisableAttribute), true).Any())
                    continue;

                var route = "/api/";

                var functionAttr = (FunctionNameAttribute)methodInfo.GetCustomAttributes(typeof(FunctionNameAttribute), false)
                    .Single();

                if (functionAttr.Name == apiDefinitionName) continue;

                HttpTriggerAttribute triggerAttribute = null;
                foreach (ParameterInfo parameter in methodInfo.GetParameters())
                {
                    triggerAttribute = parameter.GetCustomAttributes(typeof(HttpTriggerAttribute), false)
                        .FirstOrDefault() as HttpTriggerAttribute;
                    if (triggerAttribute != null) break;
                }
                if (triggerAttribute == null) continue; // Trigger attribute is required in an Azure function

                if (!string.IsNullOrWhiteSpace(triggerAttribute.Route))
                {
                    route += triggerAttribute.Route;
                }
                else
                {
                    route += functionAttr.Name;
                }

                dynamic path = new ExpandoObject();

                var verbs = triggerAttribute.Methods ?? new[] { "get", "post", "delete", "head", "patch", "put", "options" };
                foreach (string verb in verbs)
                {
                    dynamic operation = new ExpandoObject();
                    operation.operationId = ToTitleCase(functionAttr.Name);
                    operation.produces = new[] { "application/json" };
                    operation.consumes = new[] { "application/json" };
                    operation.parameters = GenerateFunctionParametersSignature(methodInfo, route, doc);

                    // Summary is title
                    operation.summary = GetFunctionName(methodInfo, functionAttr.Name);
                    // Verbose description
                    operation.description = GetFunctionDescription(methodInfo, functionAttr.Name);

                    operation.responses = GenerateResponseParameterSignature(methodInfo, doc);
                    operation.tags = new[] { apiTitle };

                    dynamic keyQuery = new ExpandoObject();
                    keyQuery.apikeyQuery = new string[0];
                    operation.security = new ExpandoObject[] { keyQuery };

                    AddToExpando(path, verb, operation);
                }
                AddToExpando(paths, route, path);
            }
            return paths;
        }

        private static string GetFunctionDescription(MethodInfo methodInfo, string funcName)
        {
            var displayAttr = (DisplayAttribute)methodInfo.GetCustomAttributes(typeof(DisplayAttribute), false)
                .SingleOrDefault();
            return !string.IsNullOrWhiteSpace(displayAttr?.Description) ? displayAttr.Description : $"This function will run {funcName}";
        }

        /// <summary>
        /// Max 80 characters in summary/title
        /// </summary>
        private static string GetFunctionName(MethodInfo methodInfo, string funcName)
        {
            var displayAttr = (DisplayAttribute)methodInfo.GetCustomAttributes(typeof(DisplayAttribute), false)
                .SingleOrDefault();
            if (!string.IsNullOrWhiteSpace(displayAttr?.Name))
            {
                return displayAttr.Name.Length > 80 ? displayAttr.Name.Substring(0, 80) : displayAttr.Name;
            }
            return $"Run {funcName}";
        }

        private static string GetPropertyDescription(PropertyInfo propertyInfo)
        {
            var displayAttr = (DisplayAttribute)propertyInfo.GetCustomAttributes(typeof(DisplayAttribute), false)
                .SingleOrDefault();
            return !string.IsNullOrWhiteSpace(displayAttr?.Description) ? displayAttr.Description : $"This returns {propertyInfo.PropertyType.Name}";
        }

        private static dynamic GenerateResponseParameterSignature(MethodInfo methodInfo, dynamic doc)
        {
            dynamic responses = new ExpandoObject();
            dynamic responseDef = new ExpandoObject();


            var returnType = methodInfo.ReturnType;
            if (returnType.IsGenericType)
            {
                var genericReturnType = returnType.GetGenericArguments().FirstOrDefault();
                if (genericReturnType != null)
                {
                    returnType = genericReturnType;
                }
            }
            if (returnType == typeof(IActionResult) || returnType == typeof(HttpResponseMessage))
            {
                var responseTypeAttr = (ProducesResponseTypeAttribute)methodInfo
                    .GetCustomAttributes(typeof(ProducesResponseTypeAttribute), false).FirstOrDefault();
                if (responseTypeAttr != null)
                {
                    returnType = responseTypeAttr.Type;
                }
                else
                {
                    returnType = typeof(void);
                }
            }
            if (returnType != typeof(void))
            {
                responseDef.schema = new ExpandoObject();

                if (returnType.Namespace == "System")
                {
                    SetParameterType(returnType, responseDef.schema, null);
                }
                else
                {
                    string name = returnType.Name;
                    if (returnType.IsGenericType)
                    {
                        var realType = returnType.GetGenericArguments()[0];
                        if (realType.Namespace == "System")
                        {
                            dynamic inlineSchema = GetObjectSchemaDefinition(null, returnType);
                            responseDef.schema = inlineSchema;
                        }
                        else
                        {
                            AddToExpando(responseDef.schema, "$ref", "#/definitions/" + name);
                            AddParameterDefinition((IDictionary<string, object>)doc.definitions, returnType);
                        }
                    }
                    else
                    {
                        AddToExpando(responseDef.schema, "$ref", "#/definitions/" + name);
                        AddParameterDefinition((IDictionary<string, object>)doc.definitions, returnType);
                    }
                }
            }

            // automatically get data(http code, description and show schema) from the new custom response class
            var responseCodes = methodInfo.GetCustomAttributes(typeof(Response), false);

            foreach (var response in responseCodes)
            {
                var customerResponse = (Response)response;

                if (!customerResponse.ShowSchema)
                    responseDef = new ExpandoObject();

                responseDef.description = customerResponse.Description;
                AddToExpando(responses, customerResponse.HttpStatusCode.ToString(), responseDef);
            }

            return responses;
        }

        private static List<object> GenerateFunctionParametersSignature(MethodInfo methodInfo, string route, dynamic doc)
        {
            var parameterSignatures = new List<object>();

            dynamic opHeaderParam = new ExpandoObject();
            opHeaderParam.name = "TouchpointId";
            opHeaderParam.@in = "header";
            opHeaderParam.required = true;
            opHeaderParam.type = "string";
            parameterSignatures.Add(opHeaderParam);

            foreach (ParameterInfo parameter in methodInfo.GetParameters())
            {
                if (parameter.ParameterType == typeof(HttpRequest)) continue;
                if (parameter.ParameterType == typeof(Microsoft.Extensions.Logging.ILogger)) continue;
                if (parameter.GetCustomAttributes().Any(attr => attr is InjectAttribute)) continue;

                bool hasUriAttribute = parameter.GetCustomAttributes().Any(attr => attr is FromQueryAttribute);

                if (route.Contains('{' + parameter.Name))
                {
                    dynamic opParam = new ExpandoObject();
                    opParam.name = parameter.Name;
                    opParam.@in = "path";
                    opParam.required = true;
                    SetParameterType(parameter.ParameterType, opParam, null);
                    parameterSignatures.Add(opParam);
                }
                else if (hasUriAttribute && parameter.ParameterType.Namespace == "System")
                {
                    dynamic opParam = new ExpandoObject();
                    opParam.name = parameter.Name;
                    opParam.@in = "query";
                    opParam.required = parameter.GetCustomAttributes().Any(attr => attr is RequiredAttribute);
                    SetParameterType(parameter.ParameterType, opParam, doc.definitions);
                    parameterSignatures.Add(opParam);
                }
                else if (hasUriAttribute && parameter.ParameterType.Namespace != "System")
                {
                    AddObjectProperties(parameter.ParameterType, "", parameterSignatures, doc);
                }
                else
                {
                    dynamic opParam = new ExpandoObject();
                    opParam.name = parameter.Name;
                    opParam.@in = "body";
                    opParam.required = true;
                    opParam.schema = new ExpandoObject();
                    if (parameter.ParameterType.Namespace == "System")
                    {
                        SetParameterType(parameter.ParameterType, opParam.schema, null);
                    }
                    else
                    {
                        AddToExpando(opParam.schema, "$ref", "#/definitions/" + parameter.ParameterType.Name);
                        AddParameterDefinition((IDictionary<string, object>)doc.definitions, parameter.ParameterType);
                    }
                    parameterSignatures.Add(opParam);
                }
            }
            return parameterSignatures;
        }

        private static void AddObjectProperties(Type t, string parentName, List<object> parameterSignatures, dynamic doc)
        {
            var publicProperties = t.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            foreach (PropertyInfo property in publicProperties)
            {
                if (!string.IsNullOrWhiteSpace(parentName))
                {
                    parentName += ".";
                }
                if (property.PropertyType.Namespace != "System")
                {
                    AddObjectProperties(property.PropertyType, parentName + property.Name, parameterSignatures, doc);
                }
                else
                {
                    dynamic opParam = new ExpandoObject();

                    opParam.name = parentName + property.Name;
                    opParam.@in = "query";
                    opParam.required = property.GetCustomAttributes().Any(attr => attr is RequiredAttribute);
                    opParam.description = GetPropertyDescription(property);
                    SetParameterType(property.PropertyType, opParam, doc.definitions);
                    parameterSignatures.Add(opParam);
                }
            }
        }

        private static void AddParameterDefinition(IDictionary<string, object> definitions, Type parameterType)
        {
            dynamic objDef;
            if (!definitions.TryGetValue(parameterType.Name, out objDef))
            {
                objDef = GetObjectSchemaDefinition(definitions, parameterType);
                definitions.Add(parameterType.Name, objDef);
            }
        }

        private static dynamic GetObjectSchemaDefinition(IDictionary<string, object> definitions, Type parameterType)
        {
            dynamic objDef = new ExpandoObject();
            objDef.type = "object";
            objDef.properties = new ExpandoObject();
            var publicProperties = parameterType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            List<string> requiredProperties = new List<string>();
            foreach (PropertyInfo property in publicProperties)
            {
                if (property.GetCustomAttributes().Any(attr => attr is RequiredAttribute))
                {
                    requiredProperties.Add(property.Name);
                }
                dynamic propDef = new ExpandoObject();
                propDef.description = GetPropertyDescription(property);

                var exampleAttribute = (Example)property.GetCustomAttributes(typeof(Example), false).FirstOrDefault();
                if (exampleAttribute != null)
                    propDef.example = exampleAttribute.Description;

                var stringAttribute = (StringLengthAttribute)property.GetCustomAttributes(typeof(StringLengthAttribute), false).FirstOrDefault();
                if (stringAttribute != null)
                {
                    propDef.maxLength = stringAttribute.MaximumLength;
                    propDef.minLength = stringAttribute.MinimumLength;
                }

                var regexAttribute = (RegularExpressionAttribute)property.GetCustomAttributes(typeof(RegularExpressionAttribute), false).FirstOrDefault();
                if (regexAttribute != null)
                    propDef.pattern = regexAttribute.Pattern;

                SetParameterType(property.PropertyType, propDef, definitions);
                AddToExpando(objDef.properties, property.Name, propDef);
            }
            if (requiredProperties.Count > 0)
            {
                objDef.required = requiredProperties;
            }
            return objDef;
        }

        private static void SetParameterType(Type parameterType, dynamic opParam, dynamic definitions)
        {
            var inputType = parameterType;
            string paramType = parameterType.UnderlyingSystemType.ToString();

            var setObject = opParam;
            if (inputType.IsArray)
            {
                opParam.type = "array";
                opParam.items = new ExpandoObject();
                setObject = opParam.items;
                parameterType = parameterType.GetElementType();
            }

            if (inputType.Namespace == "System" || (inputType.IsGenericType && inputType.GetGenericArguments()[0].Namespace == "System"))
            {
                if (paramType.Contains("System.String"))
                {
                    setObject.type = "string";
                }
                else if (paramType.Contains("System.DateTime"))
                {
                    setObject.format = "date";
                    setObject.type = "string";
                }
                else if (paramType.Contains("System.Int32"))
                {
                    setObject.format = "int32";
                    setObject.type = "integer";
                }
                else if (paramType.Contains("System.Int64"))
                {
                    setObject.format = "int64";
                    setObject.type = "integer";
                }
                else if (paramType.Contains("System.Single"))
                {
                    setObject.format = "float";
                    setObject.type = "number";
                }
                else if (paramType.Contains("System.Boolean"))
                {
                    setObject.type = "boolean";
                }
                else
                {
                    setObject.type = "string";
                }

            }

            else if (inputType.IsEnum)
            {
                opParam.type = "string";

                var enumValues = new List<string>();

                foreach (var item in Enum.GetValues(inputType))
                {
                    var memInfo = inputType.GetMember(inputType.GetEnumName(item));
                    var descriptionAttributes = memInfo[0].GetCustomAttributes(typeof(DescriptionAttribute), false);

                    var description = string.Empty;

                    if (descriptionAttributes.Length > 0)
                        description = ((DescriptionAttribute)descriptionAttributes[0]).Description;

                    if (string.IsNullOrEmpty(description))
                        description = item.ToString();

                    enumValues.Add(Convert.ToInt32(item) + " - " + description);
                }

                opParam.@enum = enumValues.ToArray();

            }
            else if (definitions != null)
            {
                AddToExpando(setObject, "$ref", "#/definitions/" + parameterType.Name);
                AddParameterDefinition((IDictionary<string, object>)definitions, parameterType);
            }
        }

        private static string ToTitleCase(string str)
        {
            return CultureInfo.CurrentCulture.TextInfo.ToTitleCase(str);
        }

        private static void AddToExpando(ExpandoObject obj, string name, object value)
        {
            if (((IDictionary<string, object>)obj).ContainsKey(name))
            {
                // Fix for functions with same routes but different verbs
                var existing = (IDictionary<string, object>)((IDictionary<string, object>)obj)[name];
                var append = (IDictionary<string, object>)value;
                foreach (KeyValuePair<string, object> keyValuePair in append)
                {
                    existing.Add(keyValuePair);
                }
            }
            else
            {
                ((IDictionary<string, object>)obj).Add(name, value);
            }
        }
    }
}