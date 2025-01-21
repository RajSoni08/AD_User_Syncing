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

class Program
{
    static async Task Main(string[] args)
    {
        var tenantId = "4f80b5dd-0f05-4843-be6d-e6544e03660a";
        var clientId = "d0ab072d-3340-4c82-86d5-c4a13661ccde";
        var clientSecret = "TZw8Q~6JfTxPx9D2f9xpQhMRiCwf93L-gtPEGcBc";// 9ebed508-df1a-4b4c-b7dc-e556edb53501
        var sqlConnectionString = "your-sql-connection-string";

        // Step 1: Authenticate and get access token from Azure AD
        var graphClient = GetGraphServiceClient(tenantId, clientId, clientSecret);

        // Step 2: Fetch all users from Azure AD
        var azureADUsers = await GetAzureADUsers(graphClient);

        // Step 3: Fetch all users from SQL Database
        var sqlUsers = GetSqlUsers(sqlConnectionString);

        // Step 4: Compare users and insert new users into SQL Database
        foreach (var azureADUser in azureADUsers)
        {
            if (!sqlUsers.Contains(azureADUser))
            {
                InsertUserToSqlDatabase(sqlConnectionString, azureADUser);
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
    static async Task<List<string>> GetAzureADUsers(GraphServiceClient graphClient)
    {
        var users = new List<string>();

        try
        {
            var usersResponse = await graphClient.Users.GetAsync();

            while (usersResponse != null)
            {
                if (usersResponse.Value != null)
                {
                    // Extract UserPrincipalName
                    users.AddRange(usersResponse.Value.Select(user => user.UserPrincipalName));
                }

                if (!string.IsNullOrEmpty(usersResponse.OdataNextLink))
                {
                    // Fetch the next page using the OdataNextLink
                    var nextPageRequest = new UserCollectionResponse
                    {
                        AdditionalData = new Dictionary<string, object>
                    {
                        {"@odata.nextLink", usersResponse.OdataNextLink }
                    }
                    };

                    usersResponse = await graphClient.Users.GetAsync(requestConfiguration =>
                    {
                        requestConfiguration.QueryParameters.Count = true;
                        requestConfiguration.Headers.Add("ConsistencyLevel", "eventual");
                    });
                }
                else
                {
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
    static List<string> GetSqlUsers(string connectionString)
    {
        var users = new List<string>();

        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();

            string query = "SELECT UserPrincipalName FROM Users";  // Change based on your table structure
            using (var command = new SqlCommand(query, connection))
            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    users.Add(reader.GetString(0));  // Assuming UserPrincipalName is a string field
                }
            }
        }

        return users;
    }

    // 4. Insert new user into SQL Database
    static void InsertUserToSqlDatabase(string connectionString, string userPrincipalName)
    {
        using (var connection = new SqlConnection(connectionString))
        {
            connection.Open();

            string query = "INSERT INTO Users (UserPrincipalName) VALUES (@UserPrincipalName)";
            using (var command = new SqlCommand(query, connection))
            {
                command.Parameters.AddWithValue("@UserPrincipalName", userPrincipalName);
                command.ExecuteNonQuery();
            }
        }

        Console.WriteLine($"User {userPrincipalName} inserted into SQL Database.");
    }
}
