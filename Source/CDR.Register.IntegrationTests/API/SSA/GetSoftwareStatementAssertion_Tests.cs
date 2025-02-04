using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using CDR.Register.IntegrationTests.Extensions;
using CDR.Register.Repository.Infrastructure;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Xunit;
using Microsoft.Extensions.Configuration;
#nullable enable

namespace CDR.Register.IntegrationTests.API.SSA
{
    /// <summary>
    /// Integration tests for GetSoftwareStatementAssertion.
    /// </summary>   
    public class GetSoftwareStatementAssertion_Tests : BaseTest
    {
        // Participation/Brand/SoftwareProduct Ids
        private string PARTICIPATIONID => GetParticipationId(BRANDID); // lookup 
        private const string BRANDID = "20C0864B-CEEF-4DE0-8944-EB0962F825EB";
        private const string SOFTWAREPRODUCTID = "86ECB655-9EBA-409C-9BE3-59E7ADF7080D";

        enum AccessTokenType
        {
            ValidAccessToken,   // Get and send valid access token
            InvalidAccessToken, // Send an invalid access token
            ExpiredAccessToken, // Send expired access token
            NoAccessToken,      // Don't send any access token
        };
        delegate void BeforeSSARequest();
        delegate void AfterSSARequest();

        private static async Task Test_GetSSA(
           string certificateFilename,
           string certificatePassword,
           HttpStatusCode expectedStatusCode,
           AccessTokenType accessTokenType = AccessTokenType.ValidAccessToken,
           BeforeSSARequest? beforeRequest = null,
           AfterSSARequest? afterRequest = null,
           string x_v = "1",
           string industry = "banking",
           string? expectedContent = null,
           string? getAccessTokenCertificateFilename = null,
           string? getAccessTokenCertificatePassword = null,
           string? expectedXV = null,
           string brandId = BRANDID,
           string softwareProductId = SOFTWAREPRODUCTID)
        {
            async Task<string?> GetAccessToken()
            {
                // Access token
                switch (accessTokenType)
                {
                    case AccessTokenType.ValidAccessToken:
                        // Get the access token with the valid certificate.
                        return await new Infrastructure.AccessToken
                        {
                            CertificateFilename = getAccessTokenCertificateFilename ?? certificateFilename,
                            CertificatePassword = getAccessTokenCertificatePassword ?? certificatePassword
                        }.GetAsync();

                    case AccessTokenType.InvalidAccessToken:
                        return "foo";

                    case AccessTokenType.ExpiredAccessToken:
                        // Represents an expired access token.
                        // "exp": 1621344825
                        // Expired at "Tuesday, May 18, 2021 11:33:45 PM GMT+10:00"
                        return "eyJhbGciOiJQUzI1NiIsImtpZCI6IkFBMjRGMTg1RUUzRjY3NTA0ODA4RkM0RTI2QjEzNUI5OUU2M0JEQTkiLCJ0eXAiOiJhdCtqd3QiLCJ4NXQiOiJxaVR4aGU0X1oxQklDUHhPSnJFMXVaNWp2YWsifQ.eyJuYmYiOjE2MjEzNDQ1MjUsImV4cCI6MTYyMTM0NDgyNSwiaXNzIjoiaHR0cHM6Ly9sb2NhbGhvc3Q6NzAwMC9pZHAiLCJhdWQiOiJjZHItcmVnaXN0ZXIiLCJjbGllbnRfaWQiOiI2ZjdhMWI4ZS04Nzk5LTQ4YTgtOTAxMS1lMzkyMDM5MWY3MTMiLCJqdGkiOiJDODRBNTM5MTA2QjI4NUJBODI2RjZGMDQ3MjU4RjBBNCIsImlhdCI6MTYyMTM0NDUyNSwic2NvcGUiOlsiY2RyLXJlZ2lzdGVyOmJhbms6cmVhZCJdLCJjbmYiOnsieDV0I1MyNTYiOiI1OEQ3NkY3QTYxQ0Q3MjZEQTFDNTRGNjg5OEU4RTY5RUE0Qzg4MDYwIn19.RTU-zrqkb-WXcJzCz62SJ4h19lj8MDyGcvLOmg0qx05WFbAsY4mEP3gsoqM1LJfq4ncw7RqSvbkCNQQ-NOnyoBHF8MGe7mzdUh3YrD0_lTg20Dkx1-l044svtP_CKTI3rXT3bZaYWce0Tb1s3mrJzfN3ja23o93FGR-wbIwHp2347b0DxjznpKBw5meLhAjS7OCx6_uMm1la6IziSQgqMd2WaA-od7w8J5br-Nn-QZZi7X1KGiPEKFDFNk8KrUdPc4NCH6t7f-Sbc34KNNEWfAOJkWdDrmsBaifSlWvSlS4nUnurGHYkmimA2JUuv3ZTqzCcLRamEER1ZoTcIs_PDw";

                    case AccessTokenType.NoAccessToken:
                        return null;

                    default:
                        throw new NotSupportedException();
                }
            }

            // Arrange
            string URL = $"{MTLS_BaseURL}/cdr-register/v1/{industry}/data-recipients/brands/{brandId}/software-products/{softwareProductId}/ssa";

            var accessToken = await GetAccessToken();

            var api = new Infrastructure.API
            {
                CertificateFilename = certificateFilename,
                CertificatePassword = certificatePassword,
                HttpMethod = HttpMethod.Get,
                URL = URL,
                AccessToken = accessToken,
                XV = x_v,
            };

            beforeRequest?.Invoke();
            try
            {
                // Act
                var response = await api.SendAsync();

                // Assert
                using (new AssertionScope())
                {
                    // Assert - Check status code
                    response.StatusCode.Should().Be(expectedStatusCode);

                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        // Assert - Check XV
                        Assert_HasHeader(expectedXV ?? x_v, response.Headers, "x-v");
                    }

                    // Assert - Check expected content
                    if (expectedContent != null)
                    {
                        await Assert_HasContent_Json(expectedContent, response.Content);
                    }
                }
            }
            finally
            {
                afterRequest?.Invoke();
            }
        }

        public async Task Test_AC01_AC02_AC03(int? XV, int expectedXV)
        {
            // Arrange - Get SoftwareProduct
            using var dbContext = new RegisterDatabaseContext(new DbContextOptionsBuilder<RegisterDatabaseContext>()
                .UseSqlite(Configuration.GetConnectionString("DefaultConnection"))
                .Options);
            var softwareProduct = dbContext.SoftwareProducts.AsNoTracking()
                    .Include(sp => sp.Brand)
                    .Where(sp => sp.SoftwareProductId == new Guid(SOFTWAREPRODUCTID))
                    .Single();

            // Arrange - Get access token
            var accessToken = await new Infrastructure.AccessToken
            {
                CertificateFilename = CERTIFICATE_FILENAME,
                CertificatePassword = CERTIFICATE_PASSWORD
            }.GetAsync();

            // Act - Send request to SSA API
            var response = await new Infrastructure.API
            {
                CertificateFilename = CERTIFICATE_FILENAME,
                CertificatePassword = CERTIFICATE_PASSWORD,
                HttpMethod = HttpMethod.Get,
                URL = $"{MTLS_BaseURL}/cdr-register/v1/banking/data-recipients/brands/{BRANDID}/software-products/{SOFTWAREPRODUCTID}/ssa",
                XV = XV.ToString(),
                AccessToken = accessToken
            }.SendAsync();

            // Assert
            using (new AssertionScope())
            {
                // Assert - Check status code
                response.StatusCode.Should().Be(HttpStatusCode.OK);

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    // Get the SSA JWKS from the Register.
                    var ssaJwks = await GetSsaJwks();

                    // Assert - Check content type
                    // Assert_HasContentType_ApplicationJson(response.Content);

                    // Assert - Check XV
                    Assert_HasHeader(expectedXV.ToString(), response.Headers, "x-v");

                    // Assert - SSA
                    var SSA = new JwtSecurityTokenHandler().ReadJwtToken(await response.Content.ReadAsStringAsync());

                    // Assert - SSA Header
                    SSA.Header.Alg.Should().Be("PS256");
                    SSA.Header.Kid.Should().Be(ssaJwks.Keys.First().Kid);
                    SSA.Header.Typ.Should().Be("JWT");

                    // Assert - SSA Claims
                    SSA.AssertClaim("iss", "cdr-register");

                    SSA.AssertClaim("iat", null);
                    SSA.AssertClaim("exp", null);
                    long iat = Convert.ToInt64(SSA.Claim("iat").Value);
                    long exp = Convert.ToInt64(SSA.Claim("exp").Value);
                    exp.Should().Be(iat + 600); // Check expiry is 10 minutes (ie 600 seconds)

                    SSA.AssertClaim("jti", null);
                    SSA.AssertClaim("org_id", softwareProduct.Brand.BrandId.ToString());
                    SSA.AssertClaim("org_name", softwareProduct.Brand.BrandName);
                    SSA.AssertClaim("client_name", softwareProduct.SoftwareProductName);
                    SSA.AssertClaim("client_description", softwareProduct.SoftwareProductDescription);
                    SSA.AssertClaim("client_uri", softwareProduct.ClientUri);
                    SSA.AssertClaim("redirect_uris", softwareProduct.RedirectUris);
                    SSA.AssertClaim("logo_uri", softwareProduct.LogoUri);
                    SSA.AssertClaim("tos_uri", softwareProduct.TosUri, true);
                    SSA.AssertClaim("policy_uri", softwareProduct.PolicyUri, true);
                    SSA.AssertClaim("jwks_uri", softwareProduct.JwksUri);
                    SSA.AssertClaim("revocation_uri", softwareProduct.RevocationUri);
                    SSA.AssertClaim("software_id", softwareProduct.SoftwareProductId.ToString());
                    SSA.AssertClaim("software_roles", "data-recipient-software-product");
                    SSA.AssertClaim("scope", softwareProduct.Scope);

                    if (XV >= 2)
                    {
                        SSA.AssertClaim("recipient_base_uri", softwareProduct.RecipientBaseUri);
                    }

                    // Assert - Validate SSA Signature
                    var validationParameters = new TokenValidationParameters()
                    {
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(2),

                        RequireSignedTokens = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = ssaJwks.Keys.First(),

                        ValidateIssuer = true,
                        ValidIssuer = "cdr-register",

                        ValidateAudience = false,
                    };

                    // Validate token (throws exception if token fails to validate)
                    new JwtSecurityTokenHandler().ValidateToken(SSA.RawData, validationParameters, out var validatedToken);
                }
            }
        }

        [Fact]
        public async Task GetSSA_WithXV2_ShouldRespondWith_200OK_V2ofSSA()
        {
            await Test_AC01_AC02_AC03(2, 2);
        }

        [Fact]
        public async Task GetSSA_WithXV1_ShouldRespondWith_200OK_V1ofSSA()
        {
            await Test_AC01_AC02_AC03(1, 1);
        }

        [Fact]
        public async Task GetSSA_WithNoXV_ShouldRespondWith_200OK_V1ofSSA()
        {
            await Test_AC01_AC02_AC03(null, 1);
        }

        [Theory]
        [InlineData(1, HttpStatusCode.OK)]        // Active
        [InlineData(2, HttpStatusCode.Forbidden)] // Removed 
        [InlineData(3, HttpStatusCode.Forbidden)] // Suspended 
        [InlineData(4, HttpStatusCode.Forbidden)] // Revoked 
        [InlineData(5, HttpStatusCode.Forbidden)] // Surrendered
        [InlineData(6, HttpStatusCode.Forbidden)] // Inactive         
        public async Task GetSSA_WithParticipationStatusNotActive_ShouldRespondWith_403Forbidden(
            int participationStatusId,
            HttpStatusCode expectedStatusCode)
        {
            int saveParticipationStatusId = GetParticipationStatusId(PARTICIPATIONID);

            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                expectedStatusCode,
                beforeRequest: () => SetParticipationStatusId(PARTICIPATIONID, participationStatusId),
                afterRequest: () => SetParticipationStatusId(PARTICIPATIONID, saveParticipationStatusId)
            );
        }

        [Theory]
        [InlineData(1, HttpStatusCode.OK)]        // Active
        [InlineData(2, HttpStatusCode.Forbidden)] // Inactive
        [InlineData(3, HttpStatusCode.Forbidden)] // Removed 
        public async Task GetSSA_WithBrandNotActive_ShouldRespondWith_403Forbidden(
            int brandStatusId,
            HttpStatusCode expectedStatusCode)
        {
            int saveBrandStatusId = GetBrandStatusId(BRANDID);

            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                expectedStatusCode,
                beforeRequest: () => SetBrandStatusId(BRANDID, brandStatusId),
                afterRequest: () => SetBrandStatusId(BRANDID, saveBrandStatusId)
            );
        }

        [Theory]
        [InlineData(1, HttpStatusCode.OK)]        // Active
        [InlineData(2, HttpStatusCode.Forbidden)] // Inactive
        [InlineData(3, HttpStatusCode.Forbidden)] // Removed 
        public async Task GetSSA_WithSoftwareProductNotActive_ShouldRespondWith_403Forbidden(
            int softwareProductStatusId,
            HttpStatusCode expectedStatusCode)
        {
            int saveSoftwareProductStatusId = GetSoftwareProductStatusId(SOFTWAREPRODUCTID);

            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                expectedStatusCode,
                beforeRequest: () => SetSoftwareProductStatusId(SOFTWAREPRODUCTID, softwareProductStatusId),
                afterRequest: () => SetSoftwareProductStatusId(SOFTWAREPRODUCTID, saveSoftwareProductStatusId)
            );
        }

        [Theory]
        [InlineData("2")]
        public async Task GetSSA_WithNoAccessToken_ShouldRespondWith_401Unauthorized(string x_v)
        {
            await Test_GetSSA(
                           CERTIFICATE_FILENAME,
                           CERTIFICATE_PASSWORD,
                           HttpStatusCode.Unauthorized,
                           AccessTokenType.NoAccessToken,
                           x_v: x_v);
        }

        [Theory]
        [InlineData("2")]
        public async Task GetSSA_WithInvalidAccessToken_ShouldRespondWith_401Unauthorized(string x_v)
        {
            await Test_GetSSA(
                           CERTIFICATE_FILENAME,
                           CERTIFICATE_PASSWORD,
                           HttpStatusCode.Unauthorized,
                           AccessTokenType.InvalidAccessToken,
                           x_v: x_v);
        }

        [Theory]
        [InlineData("2")]
        public async Task GetSSA_WithExpiredAccessToken_ShouldRespondWith_401Unauthorized(string x_v)
        {
            await Test_GetSSA(
                            CERTIFICATE_FILENAME,
                            CERTIFICATE_PASSWORD,
                            HttpStatusCode.Unauthorized,
                            AccessTokenType.ExpiredAccessToken,
                            x_v: x_v);
        }

        [Fact]
        public async Task GetSSA_DifferentHolderOfKey_ShouldRespondWith_401Unauthorized()
        {
            await Test_GetSSA(
                            ADDITIONAL_CERTIFICATE_FILENAME,
                            ADDITIONAL_CERTIFICATE_PASSWORD,
                            HttpStatusCode.Unauthorized,
                            getAccessTokenCertificateFilename: CERTIFICATE_FILENAME,
                            getAccessTokenCertificatePassword: CERTIFICATE_PASSWORD
                            );
        }

        const string EXPECTEDCONTENT_INVALIDINDUSTRY = @"
            {
                ""errors"": [
                        {
                        ""code"": ""Field/InvalidIndustry"",
                        ""title"": ""Invalid Industry"",
                        ""detail"": """",
                        ""meta"": {}
                        }
                    ]
            }";
        [Theory]
        [InlineData("", HttpStatusCode.NotFound)]
        [InlineData("foo", HttpStatusCode.BadRequest, EXPECTEDCONTENT_INVALIDINDUSTRY)]
        [InlineData("banking", HttpStatusCode.OK)]
        public async Task GetSSA_InvalidIndustry_ShouldRespondWith_400BadRequest(string industry, HttpStatusCode expectedStatusCode, string expectedContent = null)
        {
            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                expectedStatusCode,
                industry: industry,
                expectedContent: expectedContent);
        }

        const string EXPECTEDCONTENT_UNSUPPORTEDXV = @"
            {
                ""errors"": [
                    {
                    ""code"": ""Header/UnsupportedVersion"",
                    ""title"": ""Unsupported Version"",
                    ""detail"": """",
                    ""meta"": {}
                    }
                ]
            }";
        [Theory]
        [InlineData(null, HttpStatusCode.OK, null, "1")]   // omit x-v
        [InlineData("", HttpStatusCode.OK, null, "1")]     // blank x-v
        [InlineData("1", HttpStatusCode.OK)]
        [InlineData("2", HttpStatusCode.OK)]
        [InlineData("3", HttpStatusCode.NotAcceptable, EXPECTEDCONTENT_UNSUPPORTEDXV)]
        [InlineData("foo", HttpStatusCode.NotAcceptable, EXPECTEDCONTENT_UNSUPPORTEDXV)]
        public async Task GetSSA_InvalidXV_ShouldRespondWith_400BadRequest(string x_v, HttpStatusCode expectedStatusCode, string? expectedContent = null, string? expectedXV = null)
        {
            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                expectedStatusCode,
                x_v: x_v,
                expectedContent: expectedContent,
                expectedXV: expectedXV);
        }

        [Fact]
        public async Task GetSSA_WithRandomBrandId_ShouldRespondWith_404NotFound()
        {
            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                HttpStatusCode.NotFound,
                brandId: Guid.NewGuid().ToString()
            );
        }

        [Fact]
        public async Task GetSSA_WithRandomSoftwareProductId_ShouldRespondWith_404NotFound()
        {
            await Test_GetSSA(
                CERTIFICATE_FILENAME,
                CERTIFICATE_PASSWORD,
                HttpStatusCode.NotFound,
                softwareProductId: Guid.NewGuid().ToString()
            );
        }

        private static async Task<JsonWebKeySet> GetSsaJwks()
        {
            var clientHandler = new HttpClientHandler();
            clientHandler.ServerCertificateCustomValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;

            var jwksClient = new HttpClient(clientHandler);
            var jwksResponse = await jwksClient.GetAsync($"{TLS_BaseURL}/cdr-register/v1/jwks");
            return new JsonWebKeySet(await jwksResponse.Content.ReadAsStringAsync());
        }
    }
}
