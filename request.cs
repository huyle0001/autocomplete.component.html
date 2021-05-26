 static async Task<string> GetAuthorizationTokenAsync(RdsUserProfile pRdsProfile)
        {
            try
            {
                var tokenRequest = new TokenRequest
                {
                    UserName = pRdsProfile.RdsUserName,
                    Password = pRdsProfile.RdsPassword,
                    ApplicationName = pRdsProfile.RdsApplicationName,
                    ApiKey = pRdsProfile.RdsApiKey
                };

                var apiUrl = pRdsProfile.RdsApiUrl;
                var jsonData = JsonConvert.SerializeObject(tokenRequest);

                StringContent content = new StringContent(jsonData, Encoding.UTF8, "application/json");
                HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Post, apiUrl);
                request.Content = content;

                var httpClient = new HttpClient();
                var response = await httpClient.SendAsync(request);

                if (!response.IsSuccessStatusCode)
                    return null;

                var token = await response.Content.ReadAsStringAsync();

                if (string.IsNullOrWhiteSpace(token))
                    return null;

                return token;
            }
            catch (Exception ex)
            {
                LogFile.LogWriting($"         - Exception raised at {MethodBase.GetCurrentMethod().Name}: {ex.Message} ");
                throw ex;
            }
        }
        
           HttpWebRequest request  = (HttpWebRequest)WebRequest.Create(uriWebApi);

                String encoded = Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1").GetBytes(pAppProfile.ApiUser + ":" + pAppProfile.ApiPw));
                request.Headers.Add("Authorization", "Basic " + encoded);

                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    StreamReader resultStream   = new StreamReader(response.GetResponseStream());
                    lfData                      = JsonParsing.GetResponseData(resultStream, pDocType.ToUpper());
                }
