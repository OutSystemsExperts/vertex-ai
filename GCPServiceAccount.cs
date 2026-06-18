using OutSystems.ExternalLibraries.SDK;

namespace VertexAILibrary
{
    [OSStructure(
        Description = "Google Cloud service account credentials. " +
                      "Copy each value from the JSON key file downloaded in GCP Console → " +
                      "IAM & Admin → Service Accounts → Keys → Add Key → JSON. " +
                      "Store values in ODC Secrets — never hard-code private keys.")]
    public struct GCPServiceAccount
    {
        [OSStructureField(
            Description = "Service account email address ('client_email' in the key file).")]
        public string ClientEmail;

        [OSStructureField(
            Description = "RSA private key ('private_key' in the key file), including the " +
                          "-----BEGIN RSA PRIVATE KEY----- and -----END RSA PRIVATE KEY----- lines.")]
        public string PrivateKey;

        [OSStructureField(
            Description = "Private key identifier ('private_key_id' in the key file).")]
        public string PrivateKeyId;
    }
}
