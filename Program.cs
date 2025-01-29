using Microsoft.Graph;
using Microsoft.Identity.Client;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.Identity;
using Azure.Core;
using Microsoft.Graph.Models;
using System.Security.Cryptography;
using System.Text;
using Serilog;

class Program
{
    static async Task Main(string[] args)
    {
        var tenantId = "cd62b7dd-4b48-44bd-90e7-e143a22c8ead";
        var clientId = "98dc6f71-84c5-49bb-a9c8-43e39f30406d";
        var clientSecret = "VCk8Q~3DGsbdFaisBbFlP4foQtmAESsiYFe-Mbew";
        var sqlConnectionString = "Server=cilantro-db-uat.database.windows.net;Database=Cilantro3_UAT;User ID=cgiadmin_db;Password= fXO94?8cQ-`{_B=M2C+uS`&];";
        // 🔹 Azure Storage Connection String (Replace with your actual Azure Storage connection string)
        string storageConnectionString = "DefaultEndpointsProtocol=https;AccountName=yourAccountName;AccountKey=yourAccountKey;EndpointSuffix=core.windows.net";
        string containerName = "logs";
        // Step 1: Authenticate and get access token from Azure AD
        var graphClient = GetGraphServiceClient(tenantId, clientId, clientSecret);

        // Step 2: Fetch all users from Azure AD
        var azureADUsers = await GetAzureADUsers(graphClient);

        // Step 3: Fetch all users from SQL Database
        var sqlUsers = GetSqlUsers(sqlConnectionString);

        // Step 4: Compare users and insert new users into SQL Database
        foreach (var azureADUser in azureADUsers)
        {
            string userMail = azureADUser.Mail;
            string userName = azureADUser.DisplayName; // Extract Display Name

            // Check if the Mail exists in sqlUsers
            if (!sqlUsers.Any(sqlUser => sqlUser.Mail.Equals(userMail, StringComparison.OrdinalIgnoreCase)))
            {
                InsertUserToSqlDatabase(sqlConnectionString, userMail, userName);
            }
        }
    }

    // 1. Authenticate and get GraphServiceClient
    static GraphServiceClient GetGraphServiceClient(string tenantId, string clientId, string clientSecret)
    {
        var cca = ConfidentialClientApplicationBuilder.Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority(new Uri($"https://login.microsoftonline.com/{tenantId}"))
            .Build();
        var clientSecretCredential = new ClientSecretCredential(tenantId, clientId, clientSecret);
        var graphClient = new GraphServiceClient(clientSecretCredential);
        return graphClient;
    }

    // 2. Fetch all users from Azure AD
    static async Task<List<(string Mail, string DisplayName)>> GetAzureADUsers(GraphServiceClient graphClient)
    {
        var users = new List<(string Mail, string DisplayName)>();

        try
        {
            var usersResponse = await graphClient.Users.GetAsync(requestConfiguration =>
            {
                requestConfiguration.QueryParameters.Filter = $"createdDateTime ge {DateTime.UtcNow.AddDays(-1).ToString("o")}";
                requestConfiguration.QueryParameters.Select = new[] { "mail", "displayName" };
                requestConfiguration.QueryParameters.Top = 100; // Fetch 100 users per page
                requestConfiguration.QueryParameters.Count = true;
                requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
            });

            while (usersResponse != null)
            {
                if (usersResponse.Value != null)
                {
                    users.AddRange(usersResponse.Value
                        .Where(user => !string.IsNullOrEmpty(user.Mail)) // Exclude users where Mail is null
                        .Select(user => (user.Mail, user.DisplayName)));
                    break;
                }

               
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
        }

        return users;
    }


    // 3. Fetch all users from SQL Database

    static List<(string Mail, string DisplayName)> GetSqlUsers(string connectionString)
    {
        var users = new List<(string Mail, string DisplayName)>();

        try
        {
            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                string query = "SELECT EmailId, UserName FROM RBAC_All_UserMaster_AnjaniTempTesting";

                using (var command = new SqlCommand(query, connection))
                using (var reader = command.ExecuteReader())
                {
                    int emailIndex = reader.GetOrdinal("EmailId");
                    int userNameIndex = reader.GetOrdinal("UserName");

                    while (reader.Read())
                    {
                        try
                        {
                            string encryptedEmailBase64 = reader.IsDBNull(emailIndex) ? string.Empty : reader.GetString(emailIndex);
                            string displayName = reader.IsDBNull(userNameIndex) ? string.Empty : reader.GetString(userNameIndex);

                            if (!string.IsNullOrWhiteSpace(encryptedEmailBase64))
                            {
                                try
                                {
                                    byte[] encryptedMail = EmailEncryption.StringToByteArray(encryptedEmailBase64); // Convert Hex to Byte Array
                                    string decryptedMail = EmailEncryption.DecryptEmail(encryptedMail); // Pass Byte Array to Decrypt
                                    users.Add((decryptedMail, displayName));
                                }
                                catch (FormatException)
                                {
                                    Console.WriteLine("Invalid Hex encoding detected. Skipping entry.");
                                    continue;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Error: {ex.Message}");
                        }

                    }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Database error: {ex.Message}");
        }

        return users;
    }
    static void InsertUserToSqlDatabase(string connectionString, string userPrincipalName, string displayName)
    {
        try
        {
            // Encrypt the UserPrincipalName (email) before inserting
            string encryptedEmail = EmailEncryption.EncryptEmail(userPrincipalName);

            using (var connection = new SqlConnection(connectionString))
            {
                connection.Open();

                // Adjust the query to insert both EncryptedEmail and DisplayName
                string query = @"
                INSERT INTO RBAC_All_UserMaster_AnjaniTempTesting (EmailId, UserName, IsActive, CreatedBy, CreatedDate) 
                VALUES (@EmailId, @UserName, 1, 'AD Sync', GETUTCDATE())";

                using (var command = new SqlCommand(query, connection))
                {
                    // Add parameters
                    command.Parameters.AddWithValue("@EmailId", encryptedEmail);
                    command.Parameters.AddWithValue("@UserName", displayName); // Handle null names

                    // Execute the query
                    command.ExecuteNonQuery();
                }
            }

            Console.WriteLine($"User {userPrincipalName} inserted into SQL Database.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error inserting user: {ex.Message}");
        }
    }



}
