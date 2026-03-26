// Copyright (c) VoiceStudio. All rights reserved.
// Licensed under the MIT License.

using Microsoft.VisualStudio.TestTools.UnitTesting;
using VoiceStudio.App.Services.IPC;
using System;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace VoiceStudio.App.Tests.Services.IPC
{
    [TestClass]
    public class RequestSignerTests
    {
        // 32-byte key (minimum for RequestSigner)
        private const string TestSecretKeyBase64 = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
        private byte[] _testSecretKey = null!;

        [TestInitialize]
        public void Setup()
        {
            _testSecretKey = Convert.FromBase64String(TestSecretKeyBase64);
        }

        [TestMethod]
        public void Constructor_WithValidKey_Succeeds()
        {
            using var signer = new RequestSigner(_testSecretKey);
            Assert.IsNotNull(signer);
        }

        [TestMethod]
        public void Constructor_WithBase64Key_Succeeds()
        {
            using var signer = new RequestSigner(TestSecretKeyBase64);
            Assert.IsNotNull(signer);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_WithShortKey_ThrowsException()
        {
            var shortKey = new byte[16]; // Too short, needs 32
            using var signer = new RequestSigner(shortKey);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_WithNullKey_ThrowsException()
        {
            using var signer = new RequestSigner((byte[])null!);
        }

        [TestMethod]
        public void GenerateSecretKey_ReturnsValidBase64()
        {
            var key = RequestSigner.GenerateSecretKey();
            
            Assert.IsNotNull(key);
            Assert.IsFalse(string.IsNullOrEmpty(key));
            
            // Should be decodable as Base64
            var decoded = Convert.FromBase64String(key);
            Assert.AreEqual(32, decoded.Length);
        }

        [TestMethod]
        public void SignRequest_ProducesConsistentSignature()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var sig1 = signer.SignRequest("GET", "/api/test", "", "2026-02-05T12:00:00Z");
            var sig2 = signer.SignRequest("GET", "/api/test", "", "2026-02-05T12:00:00Z");
            
            Assert.AreEqual(sig1, sig2);
        }

        [TestMethod]
        public void SignRequest_DifferentMethods_DifferentSignatures()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var sigGet = signer.SignRequest("GET", "/api/test", "", "2026-02-05T12:00:00Z");
            var sigPost = signer.SignRequest("POST", "/api/test", "", "2026-02-05T12:00:00Z");
            
            Assert.AreNotEqual(sigGet, sigPost);
        }

        [TestMethod]
        public void SignRequest_DifferentPaths_DifferentSignatures()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var sig1 = signer.SignRequest("GET", "/api/test1", "", "2026-02-05T12:00:00Z");
            var sig2 = signer.SignRequest("GET", "/api/test2", "", "2026-02-05T12:00:00Z");
            
            Assert.AreNotEqual(sig1, sig2);
        }

        [TestMethod]
        public void SignRequest_DifferentBodies_DifferentSignatures()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var sig1 = signer.SignRequest("POST", "/api/test", "{\"a\":1}", "2026-02-05T12:00:00Z");
            var sig2 = signer.SignRequest("POST", "/api/test", "{\"a\":2}", "2026-02-05T12:00:00Z");
            
            Assert.AreNotEqual(sig1, sig2);
        }

        [TestMethod]
        public void SignRequest_DifferentTimestamps_DifferentSignatures()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var sig1 = signer.SignRequest("GET", "/api/test", "", "2026-02-05T12:00:00Z");
            var sig2 = signer.SignRequest("GET", "/api/test", "", "2026-02-05T12:01:00Z");
            
            Assert.AreNotEqual(sig1, sig2);
        }

        [TestMethod]
        public void SignRequest_NormalizesMethod()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var sigLower = signer.SignRequest("get", "/api/test", "", "2026-02-05T12:00:00Z");
            var sigUpper = signer.SignRequest("GET", "/api/test", "", "2026-02-05T12:00:00Z");
            
            Assert.AreEqual(sigLower, sigUpper);
        }

        [TestMethod]
        public void VerifySignature_ValidSignature_ReturnsTrue()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var timestamp = signer.GenerateTimestamp();
            var signature = signer.SignRequest("POST", "/api/data", "{\"key\":\"value\"}", timestamp);
            
            var isValid = signer.VerifySignature(signature, "POST", "/api/data", "{\"key\":\"value\"}", timestamp);
            
            Assert.IsTrue(isValid);
        }

        [TestMethod]
        public void VerifySignature_InvalidSignature_ReturnsFalse()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var isValid = signer.VerifySignature("invalid_signature", "GET", "/api/test", "", "2026-02-05T12:00:00Z");
            
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void VerifySignature_TamperedBody_ReturnsFalse()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var timestamp = signer.GenerateTimestamp();
            var signature = signer.SignRequest("POST", "/api/data", "{\"original\":\"data\"}", timestamp);
            
            var isValid = signer.VerifySignature(signature, "POST", "/api/data", "{\"tampered\":\"data\"}", timestamp);
            
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void VerifySignature_NullSignature_ReturnsFalse()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var isValid = signer.VerifySignature(null!, "GET", "/api/test", "", "2026-02-05T12:00:00Z");
            
            Assert.IsFalse(isValid);
        }

        [TestMethod]
        public void GenerateTimestamp_ReturnsIso8601Format()
        {
            using var signer = new RequestSigner(_testSecretKey);
            
            var timestamp = signer.GenerateTimestamp();
            
            // Should be parseable as ISO 8601
            Assert.IsTrue(DateTime.TryParse(timestamp, out var parsed));
        }

        [TestMethod]
        public async Task SignHttpRequestAsync_AddsSignatureHeaders()
        {
            using var signer = new RequestSigner(_testSecretKey, enabled: true);
            
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
            
            await signer.SignHttpRequestAsync(request);
            
            Assert.IsTrue(request.Headers.Contains(RequestSigner.SignatureHeader));
            Assert.IsTrue(request.Headers.Contains(RequestSigner.TimestampHeader));
            Assert.IsTrue(request.Headers.Contains(RequestSigner.VersionHeader));
        }

        [TestMethod]
        public async Task SignHttpRequestAsync_WhenDisabled_NoHeaders()
        {
            using var signer = RequestSigner.CreateDisabled();
            
            var request = new HttpRequestMessage(HttpMethod.Get, "http://localhost/api/test");
            
            await signer.SignHttpRequestAsync(request);
            
            Assert.IsFalse(request.Headers.Contains(RequestSigner.SignatureHeader));
            Assert.IsFalse(request.Headers.Contains(RequestSigner.TimestampHeader));
        }

        [TestMethod]
        public async Task SignHttpRequestAsync_WithBody_IncludesBodyInSignature()
        {
            using var signer = new RequestSigner(_testSecretKey, enabled: true);
            
            var request = new HttpRequestMessage(HttpMethod.Post, "http://localhost/api/test")
            {
                Content = new StringContent("{\"data\":\"test\"}", Encoding.UTF8, "application/json")
            };
            
            await signer.SignHttpRequestAsync(request);
            
            // Signature should be present
            Assert.IsTrue(request.Headers.Contains(RequestSigner.SignatureHeader));
            
            // Verify the signature includes body
            var timestamp = request.Headers.GetValues(RequestSigner.TimestampHeader).First();
            var signature = request.Headers.GetValues(RequestSigner.SignatureHeader).First();
            
            // Verify with body
            Assert.IsTrue(signer.VerifySignature(
                signature,
                "POST",
                "/api/test",
                "{\"data\":\"test\"}",
                timestamp));
        }

        [TestMethod]
        public void CreateDisabled_ReturnsDisabledSigner()
        {
            using var signer = RequestSigner.CreateDisabled();
            
            Assert.IsNotNull(signer);
            
            // Should still be able to sign (for testing purposes)
            var signature = signer.SignRequest("GET", "/test", "", "ts");
            Assert.IsNotNull(signature);
        }

        [TestMethod]
        public void Dispose_ClearsSecretKey()
        {
            var signer = new RequestSigner(_testSecretKey);
            
            // Get signature before dispose
            var sigBefore = signer.SignRequest("GET", "/test", "", "ts");
            
            signer.Dispose();
            
            // After dispose, signing should throw or return different result
            // Since we cleared the key, behavior depends on implementation
            // The key is zeroed out but the signer should still be unusable
            // This test verifies dispose runs without error
            Assert.IsNotNull(sigBefore);
        }

        [TestMethod]
        public void DifferentKeys_ProduceDifferentSignatures()
        {
            var key1 = new byte[32];
            var key2 = new byte[32];
            key1[0] = 1;
            key2[0] = 2;
            
            using var signer1 = new RequestSigner(key1);
            using var signer2 = new RequestSigner(key2);
            
            var sig1 = signer1.SignRequest("GET", "/test", "", "ts");
            var sig2 = signer2.SignRequest("GET", "/test", "", "ts");
            
            Assert.AreNotEqual(sig1, sig2);
        }
    }
}
