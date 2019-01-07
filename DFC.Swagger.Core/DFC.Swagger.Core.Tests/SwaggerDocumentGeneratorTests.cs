using System;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Internal;
using NUnit.Framework;

namespace DFC.Swagger.Core.Tests
{
    public class SwaggerDocumentGeneratorTests
    {
        private HttpRequest _request;
        private const string ApiTitle = "OpenAPI 2 - Swagger";
        private const string ApiDescription = ApiTitle + " Description";
        private const string ApiDefinitionName = "Swagger Generator";

        [SetUp]
        public void Setup()
        {
            _request = new DefaultHttpRequest(new DefaultHttpContext())
            {
                ContentType = "application/json",
            };
        }

        [Test]
        public void SwaggerDocumentGenerator_WhenCalledWithNullHttpRequest_ThrowsArgumentNullException()
        {
            Assert.That(() => SwaggerDocumentGenerator.GenerateSwaggerDocument(null, ApiTitle, ApiDescription, ApiDefinitionName),
                Throws.Exception
                    .TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SwaggerDocumentGenerator_WhenCalledWithNullTitle_ThrowsArgumentNullException()
        {
            Assert.That(() => SwaggerDocumentGenerator.GenerateSwaggerDocument(_request, null, ApiDescription, ApiDefinitionName),
                Throws.Exception
                    .TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SwaggerDocumentGenerator_WhenCalledWithNullAPIDescription_ThrowsArgumentNullException()
        {
            Assert.That(() => SwaggerDocumentGenerator.GenerateSwaggerDocument(_request, ApiTitle, null, ApiDefinitionName),
                Throws.Exception
                    .TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SwaggerDocumentGenerator_WhenCalledWithNullApiDefinitionName_ThrowsArgumentNullException()
        {
            Assert.That(() => SwaggerDocumentGenerator.GenerateSwaggerDocument(_request, ApiTitle, ApiDescription, null),
                Throws.Exception
                    .TypeOf<ArgumentNullException>());
        }

        [Test]
        public void SwaggerDocumentGenerator_WhenCalledWithValidParams_ReturnsSwaggerDoc()
        {
            var response =
                SwaggerDocumentGenerator.GenerateSwaggerDocument(_request, ApiTitle, ApiDescription, ApiDefinitionName);

            Assert.IsNotNull(response);
        }
    }
}