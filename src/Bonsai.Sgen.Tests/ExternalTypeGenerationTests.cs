using Microsoft.VisualStudio.TestTools.UnitTesting;
using NJsonSchema;

namespace Bonsai.Sgen.Tests
{
    [TestClass]
    public class ExternalTypeGenerationTests
    {
        private static async Task<JsonSchema> CreateCommonDefinitions()
        {
            return await JsonSchema.FromJsonAsync(@"
{
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""definitions"": {
      ""CommonType"": {
        ""type"": ""object"",
        ""properties"": {
          ""Bar"": {
            ""type"": ""integer""
          }
        }
      }
    }
  }
");
        }

        [TestMethod]
        public void GenerateWithEmptyDefinitions_EmitEmptyCodeBlock()
        {
            var schema = new JsonSchema();
            var generator = TestHelper.CreateGenerator(schema);
            var code = generator.GenerateFile();
            CompilerTestHelper.CompileFromSource(code);
        }

        [TestMethod]
        public async Task GenerateWithDefinitionsOnly_EmitDefinitionTypes()
        {
            var schema = await CreateCommonDefinitions();
            var generator = TestHelper.CreateGenerator(schema);
            var code = generator.GenerateFile();
            Assert.IsTrue(code.Contains("public partial class CommonType"), "Missing type definition.");
            CompilerTestHelper.CompileFromSource(code);
        }

        [TestMethod]
        public async Task GenerateWithExternalTypeReferenceProperty_OmitExternalTypeDefinition()
        {
            var schemaA = await CreateCommonDefinitions();
            var generatorA = TestHelper.CreateGenerator(schemaA, schemaNamespace: $"{nameof(TestHelper)}.Base");
            var codeA = generatorA.GenerateFile();

            var schemaB = await JsonSchema.FromJsonAsync(@"
{
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""definitions"": {
      ""SpecificType"": {
        ""type"": ""object"",
        ""properties"": {
          ""Bar"": {
            ""$ref"": ""https://schemaA/definitions/CommonType""
          },
          ""Baz"": {
            ""type"": ""integer""
          }
        }
      }
    }
  }
",
documentPath: "",
schema => new TestJsonReferenceResolver(
    new JsonSchemaAppender(schema, new DefaultTypeNameGenerator()),
    schemaA,
    generatorA.Settings.Namespace));

            var generatorB = TestHelper.CreateGenerator(schemaB, schemaNamespace: $"{nameof(TestHelper)}.Derived");
            var codeB = generatorB.GenerateFile();
            Assert.IsTrue(codeB.Contains("public TestHelper.Base.CommonType Bar"), "Incorrect type declaration.");
            CompilerTestHelper.CompileFromSource(codeA, codeB);
        }

        [TestMethod]
        public async Task GenerateWithExternalBaseTypeReference_OmitExternalTypeDefinition()
        {
            var schemaA = await CreateCommonDefinitions();
            var generatorA = TestHelper.CreateGenerator(schemaA, schemaNamespace: $"{nameof(TestHelper)}.Base");
            var codeA = generatorA.GenerateFile();

            var schemaB = await JsonSchema.FromJsonAsync(@"
{
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""definitions"": {
      ""DerivedType"": {
        ""type"": ""object"",
        ""properties"": {
          ""Baz"": {
            ""type"": [
              ""null"",
              ""integer""
            ]
          }
        },
        ""allOf"": [
          {
            ""$ref"": ""https://schemaA/definitions/CommonType""
          }
        ]
      }
    }
  }
",
documentPath: "",
schema => new TestJsonReferenceResolver(
    new JsonSchemaAppender(schema, new DefaultTypeNameGenerator()),
    schemaA,
    generatorA.Settings.Namespace));

            var generatorB = TestHelper.CreateGenerator(schemaB, schemaNamespace: $"{nameof(TestHelper)}.Derived");
            var codeB = generatorB.GenerateFile();
            Assert.IsTrue(codeB.Contains("class DerivedType : TestHelper.Base.CommonType"), "Incorrect type declaration.");
            CompilerTestHelper.CompileFromSource(codeA, codeB);
        }

        [TestMethod]
        public async Task GenerateWithInternalTypeReference_EmitInternalTypeDefinition()
        {
            var schemaNamespace = $"{nameof(TestHelper)}.Derived";
            var schemaA = await CreateCommonDefinitions();
            var schemaB = await JsonSchema.FromJsonAsync(@"
{
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""definitions"": {
      ""SpecificType"": {
        ""type"": ""object"",
        ""properties"": {
          ""Bar"": {
            ""$ref"": ""https://schemaA/definitions/CommonType""
          },
          ""Baz"": {
            ""type"": ""integer""
          }
        }
      }
    }
  }
",
            documentPath: "",
            schema => new TestJsonReferenceResolver(
                new JsonSchemaAppender(schema, new DefaultTypeNameGenerator()),
                schemaA,
                schemaNamespace));

            var generatorB = TestHelper.CreateGenerator(schemaB, schemaNamespace: schemaNamespace);
            var codeB = generatorB.GenerateFile();
            Assert.IsTrue(codeB.Contains("public partial class CommonType"), "Missing internal type definition.");
            CompilerTestHelper.CompileFromSource(codeB);
        }

        [TestMethod]
        public void GenerateWithExternalDiscriminatorReferenceProperty_OmitExternalDiscriminatorDefinition()
        {
            var derivedSchemas = SchemaTestHelper.CreateDerivedSchemas("kind", "Dog", "Cat");
            var discriminator = SchemaTestHelper.CreateDiscriminatorSchema<JsonSchemaProperty>("kind", derivedSchemas);
            var schema = SchemaTestHelper.CreateContainerSchema(derivedSchemas);
            schema.Properties.Add("Animal", discriminator);

            var schemaProperty = schema.Properties.First();
            foreach (var definition in derivedSchemas.Prepend(new(schemaProperty.Key, schemaProperty.Value)))
            {
                definition.Value.ExtensionData ??= new Dictionary<string, object>();
                definition.Value.ExtensionData[JsonSchemaExtensions.TypeNameAnnotation] = $"TestHelper.Base.{definition.Key}";
            }

            var generator = TestHelper.CreateGenerator(schema, schemaNamespace: nameof(TestHelper) + ".Derived");
            var code = generator.GenerateFile();
            Assert.IsTrue(code.Contains("public TestHelper.Base.Animal Animal"), "Container must reference external type.");
            Assert.IsTrue(!code.Contains("public partial class Animal"), "External discriminator base type should not be generated.");

            const string externalCode = @"
            namespace TestHelper.Base
            {
                public class Animal { }
                public class Dog : Animal { }
                public class Cat : Animal { }
            }
            ";
            CompilerTestHelper.CompileFromSource(externalCode, code);
        }

        private static async Task<JsonSchema> CreateScalarDefinitionSchema(string definition, string propertyType)
        {
            return await JsonSchema.FromJsonAsync($@"
{{
    ""$schema"": ""http://json-schema.org/draft-04/schema#"",
    ""definitions"": {{
      ""Lines"": {definition},
      ""Holder"": {{
        ""type"": ""object"",
        ""properties"": {{
          ""Bar"": {propertyType}
        }}
      }}
    }}
  }}
");
        }

        const string ExternalScalarCode = @"
            namespace TestHelper.Base
            {
                [System.Flags]
                public enum Lines { A = 1, B = 2 }
            }
            ";

        [TestMethod]
        public async Task GenerateWithExternalScalarTypeReference_EmitExternalTypeName()
        {
            var schema = await CreateScalarDefinitionSchema(
                @"{ ""type"": ""string"", ""x-sgen-typename"": ""TestHelper.Base.Lines"" }",
                @"{ ""$ref"": ""#/definitions/Lines"" }");
            var generator = TestHelper.CreateGenerator(schema, schemaNamespace: nameof(TestHelper) + ".Derived");
            var code = generator.GenerateFile();
            Assert.IsTrue(code.Contains("public TestHelper.Base.Lines Bar"), "Property must reference external type.");
            Assert.IsTrue(!code.Contains("class Lines"), "External scalar type should not be generated.");
            CompilerTestHelper.CompileFromSource(ExternalScalarCode, code);
        }

        [TestMethod]
        public async Task GenerateWithExternalNullableScalarTypeReference_EmitNullableExternalTypeName()
        {
            var schema = await CreateScalarDefinitionSchema(
                @"{ ""type"": ""string"", ""x-sgen-typename"": ""TestHelper.Base.Lines"" }",
                @"{ ""oneOf"": [ { ""$ref"": ""#/definitions/Lines"" }, { ""type"": ""null"" } ] }");
            var generator = TestHelper.CreateGenerator(schema, schemaNamespace: nameof(TestHelper) + ".Derived");
            var code = generator.GenerateFile();
            Assert.IsTrue(code.Contains("public TestHelper.Base.Lines? Bar"), "Property must reference nullable external type.");
            CompilerTestHelper.CompileFromSource(ExternalScalarCode, code);
        }

        [TestMethod]
        public async Task GenerateWithExternalIntegerTypeReference_EmitExternalTypeName()
        {
            var schema = await CreateScalarDefinitionSchema(
                @"{ ""type"": ""integer"", ""x-sgen-typename"": ""System.UInt16"" }",
                @"{ ""$ref"": ""#/definitions/Lines"" }");
            var generator = TestHelper.CreateGenerator(schema);
            var code = generator.GenerateFile();
            Assert.IsTrue(code.Contains("public ushort Bar"), "Property must reference external type.");
            CompilerTestHelper.CompileFromSource(code);
        }

        [TestMethod]
        public async Task GenerateWithInternalScalarTypeReference_EmitPrimitiveType()
        {
            var schemaNamespace = nameof(TestHelper) + ".Derived";
            var schema = await CreateScalarDefinitionSchema(
                $@"{{ ""type"": ""string"", ""x-sgen-typename"": ""{schemaNamespace}.Lines"" }}",
                @"{ ""$ref"": ""#/definitions/Lines"" }");
            var generator = TestHelper.CreateGenerator(schema, schemaNamespace: schemaNamespace);
            var code = generator.GenerateFile();
            Assert.IsTrue(code.Contains("public string Bar"), "Scalar named in the generated namespace must stay primitive.");
            CompilerTestHelper.CompileFromSource(code);
        }
    }
}
