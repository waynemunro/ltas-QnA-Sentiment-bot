// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.QnA;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.Http.Extensions;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Microsoft.BotBuilderSamples
{
    public class QnABot : ActivityHandler
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<QnABot> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public QnABot(IConfiguration configuration, ILogger<QnABot> logger, IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();

            var qnaMaker = new QnAMaker(new QnAMakerEndpoint
            {
                KnowledgeBaseId = _configuration["QnAKnowledgebaseId"],
                EndpointKey = _configuration["QnAAuthKey"],
                Host = GetHostname()
            },
            null,
            httpClient);

            // call the sentiment 

            var client = new HttpClient();

            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", _configuration["OcpApimSubscriptionKey"]);

            client.BaseAddress = new System.Uri("https://westus.api.cognitive.microsoft.com");

            var question = turnContext.Activity.Text;

            

            if (question == "angry" || question == "unhappy" )
            {
                var sentimentMessage = "\n\nSorry to see you are stressed 🙁. We recommend you contact our service center for counseling.";
                var activity = MessageFactory.SuggestedActions(
                new CardAction[]
                {
                    new CardAction(title: "contact our support center", type: ActionTypes.OpenUrl, value: "http://www.ltas.co.za/contact/")
                }, text: $"{sentimentMessage} Contact us");

                // Send the activity as a reply to the user.
                await turnContext.SendActivityAsync(activity);
                return;
            }
            else if(question == "alright" || question == "happy" || question == "love")
            {
                await turnContext.SendActivityAsync(MessageFactory.Text("😃"), cancellationToken);
                return;
            }

            var body = "{\"documents\":[{\"id\": \"1\",\"language\":\"en\",\"text\": \"" + question + "\"}]}";

            var content = new StringContent(body, Encoding.UTF8, "application/json");

            var sentimentresult = await client.PostAsync("/text/analytics/v2.0/sentiment", content);

            string sentimentResultDoc = "";

            if (sentimentresult.IsSuccessStatusCode)
            {
                _logger.LogInformation("Got response from sentiment");

                sentimentResultDoc = await sentimentresult.Content.ReadAsStringAsync();

                _logger.LogInformation(sentimentResultDoc);
            }

            _logger.LogInformation("Calling QnA Maker");

            // The actual call to the QnA Maker service.
            var response = await qnaMaker.GetAnswersAsync(turnContext);

            
            
            if (response != null && response.Length > 0)
            {
                var answer = MessageFactory.Text(response[0].Answer);

                dynamic stuff = JObject.Parse(sentimentResultDoc);

                var score = stuff.documents[0].score;

                if (score < 0.5)
                {
                    // Create the activity and add suggested actions.
                    var activity = MessageFactory.SuggestedActions(
                        new CardAction[]
                        {
                           new CardAction(title: "😡", type: ActionTypes.ImBack, value: "angry"),
                           new CardAction(title: "🙁", type: ActionTypes.ImBack, value: "unhappy"),
                           new CardAction(title: "😐", type: ActionTypes.ImBack, value: "alright"),
                           new CardAction(title: "😃", type: ActionTypes.ImBack, value: "happy"),
                           new CardAction(title: "😍", type: ActionTypes.ImBack, value: "love")

                        }, text: $"{response[0].Answer}\nhow do you feel?");

                    // Send the activity as a reply to the user.
                    await turnContext.SendActivityAsync(activity);
                    // _ = MessageFactory.Text(response[0].Answer + sentimentMessage);

                }
                else
                {
                    await turnContext.SendActivityAsync(answer, cancellationToken);
                }
            }
            else
            {
                await turnContext.SendActivityAsync(MessageFactory.Text("No QnA Maker answers were found."), cancellationToken);
            }
        }

        private string GetHostname()
        {
            var hostname = _configuration["QnAEndpointHostName"];
            if (!hostname.StartsWith("https://"))
            {
                hostname = string.Concat("https://", hostname);
            }

            if (!hostname.EndsWith("/qnamaker"))
            {
                hostname = string.Concat(hostname, "/qnamaker");
            }

            return hostname;
        }
    }
}
