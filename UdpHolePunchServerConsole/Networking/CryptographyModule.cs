﻿using System;
using System.IO;
using System.Security.Cryptography;

namespace Networking
{
    public sealed class CryptographyModule
    {
        private readonly ECDiffieHellmanCng _ecdh;
        private readonly CngKey _signature;
        private readonly byte[] _publicKey;
        private readonly byte[] _signaturePublicKey;
        private byte[] _privateKey;
        private byte[] _recipientsSignaturePublicKey;
        private bool _isEnabled;

        public CryptographyModule()
        {
            _ecdh = new ECDiffieHellmanCng();
            _ecdh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
            _ecdh.HashAlgorithm = CngAlgorithm.Sha256;
            _signature = CngKey.Create(CngAlgorithm.ECDsaP256);

            _publicKey = _ecdh.PublicKey.ToByteArray();
            _signaturePublicKey = _signature.Export(CngKeyBlobFormat.GenericPublicBlob);

            _privateKey = Array.Empty<byte>();
            _recipientsSignaturePublicKey = Array.Empty<byte>();

            _isEnabled = false;
        }

        public byte[] PublicKey => (byte[])_publicKey.Clone();
        public byte[] SignaturePublicKey => (byte[])_signaturePublicKey.Clone();
        public bool IsEnabled => _isEnabled;

        public bool TrySetKeys(byte[] publicKey, byte[] signaturePublicKey)
        {
            if (_isEnabled)
            {
                return false;
            }

            try
            {
                _privateKey = _ecdh.DeriveKeyMaterial(CngKey.Import(publicKey, CngKeyBlobFormat.EccPublicBlob));
                _recipientsSignaturePublicKey = (byte[])signaturePublicKey.Clone();
                _isEnabled = true;

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TryCreateSignature(byte[] data, out byte[] dataSignature)
        {
            if (!_isEnabled)
            {
                dataSignature = Array.Empty<byte>();

                return false;
            }

            try
            {
                var signingAlgorithm = new ECDsaCng(_signature);
                dataSignature = signingAlgorithm.SignData(data);
                signingAlgorithm.Clear();

                return true;
            }
            catch (Exception)
            {
                dataSignature = Array.Empty<byte>();
        
                return false;
            }
        }

        public bool TryVerifySignature(byte[] data, byte[] signature)
        {
            if (!_isEnabled)
            {
                return false;
            }

            try
            {
                using var key = CngKey.Import(_recipientsSignaturePublicKey, CngKeyBlobFormat.GenericPublicBlob);
                var signingAlgorithm = new ECDsaCng(key);
                var result = signingAlgorithm.VerifyData(data, signature);
                signingAlgorithm.Clear();

                return result;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public bool TryEncrypt(byte[] secretMessage, out byte[] encryptedMessage, out byte[] iv)
        {
            if (!_isEnabled)
            {
                iv = Array.Empty<byte>();
                encryptedMessage = Array.Empty<byte>();

                return false;
            }

            try
            {
                using Aes aes = new AesCryptoServiceProvider();
                aes.Key = _privateKey;
                iv = aes.IV;
                using var cipherText = new MemoryStream();
                using var cryptoStream = new CryptoStream(cipherText, aes.CreateEncryptor(), CryptoStreamMode.Write);
                cryptoStream.Write(secretMessage, 0, secretMessage.Length);
                cryptoStream.Close();
                encryptedMessage = cipherText.ToArray();

                return true;
            }
            catch (Exception)
            {
                iv = Array.Empty<byte>();
                encryptedMessage = Array.Empty<byte>();

                return false;
            }
        }

        public bool TryDecrypt(byte[] encryptedMessage, byte[] iv, out byte[] decryptedMessage)
        {
            if (!_isEnabled)
            {
                decryptedMessage = Array.Empty<byte>();

                return false;
            }

            try
            {
                using Aes aes = new AesCryptoServiceProvider();
                aes.Key = _privateKey;
                aes.IV = iv;
                using var decryptionStream = new MemoryStream();
                using var cryptoStream = new CryptoStream(decryptionStream, aes.CreateDecryptor(), CryptoStreamMode.Write);
                cryptoStream.Write(encryptedMessage, 0, encryptedMessage.Length);
                cryptoStream.Close();
                decryptedMessage = decryptionStream.ToArray();

                return true;
            }
            catch (Exception)
            {
                decryptedMessage = Array.Empty<byte>();

                return false;
            }
        }

        public void Disable()
        {
            _isEnabled = false;
        }

        public static bool TryComputeFileHash(string path, out string fileHash)
        {
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 10 * 1024 * 1024);
                using var sha = SHA256.Create();
                byte[] hash = sha.ComputeHash(fs);
                fileHash = BitConverter.ToString(hash).ToLower().Replace("-", "");

                return true;
            }
            catch (Exception)
            {
                fileHash = string.Empty;

                return false;
            }
        }
    }
}