﻿using TravelfinderAPI.GmpGis;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using TiktokenSharp;

namespace TravelfinderAPI.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class ChatController : ControllerBase
    {
        private GmpGisApiClient _gmpGisApiClient;
        private ArcGisApiClient _arcGisApiClient;
        private readonly ILogger<ChatController> _logger;
        private string _openAiKey;
        private PromotTemplate[] _promotTemplate;
        private readonly bool _enableProxy;
        private OpenAIApiClient _openAIApiClient;

        public ChatController(ILogger<ChatController> logger, IConfiguration configuration, GmpGisApiClient gmpGisApiClient, ArcGisApiClient arcGisApiClient)
        {
            _logger = logger;
            _openAiKey = configuration["OPENAI_API_KEY"];
            _enableProxy = Convert.ToBoolean(configuration["ENABLE_PROXY"]);
            _promotTemplate = configuration.GetSection("PROMOT_TEMPLATE").Get<PromotTemplate[]>();

            _gmpGisApiClient = gmpGisApiClient;
            _arcGisApiClient = arcGisApiClient;

            _openAIApiClient = new OpenAIApiClient(_openAiKey, _enableProxy, arcGisApiClient);
        }

        [HttpGet]
        public async Task Get(string prompt)
        {
            await HttpContext.SSEInitAsync();

            var responseStream = await _openAIApiClient.SendPromptStream(prompt);

            using (var reader = new StreamReader(responseStream))
            {
                while (!reader.EndOfStream)
                {
                    string data = reader.ReadLine();
                    
                    await HttpContext.SSESendDataAsync(data);
                }
            }

        }
        [HttpPost]
        [Route("StreamCommand")]
        public async Task StreamCommand([FromBody] MessageRequest messageRequest)
        {
            await HttpContext.SSEInitAsync();

            var apiKey = Request.Headers["Joi-ApiKey"];

            if (!string.IsNullOrEmpty(apiKey))
            {
                _openAiKey = apiKey;
            }

            var placeResult1 = await _gmpGisApiClient.NearPoint(messageRequest.Latitude, messageRequest.Longitude, 1000, "en-us", 20);

            var placeResult2 = await _arcGisApiClient.NearPoint(messageRequest.Latitude, messageRequest.Longitude, 1000, 20);

            var placeResult = new GmpGis.PlaceResult();
            placeResult.Places = placeResult1.Places.Union(placeResult2.Places).ToArray();

            var messages = messageRequest.Messages;
            var systemMessage = _promotTemplate.FirstOrDefault(x => x.Id == messageRequest.SystemId);

            var promot = systemMessage == null ? "You are ChatGPT, a large language model trained by OpenAI. Follow the user's instructions carefully. Respond using markdown format." : systemMessage.Promot;

            messages = messages.Prepend(new Message()
                {
                    Role = "assistant",
                    Content = "Sure, please provide further description."
                }).Prepend(new Message()
                {
                    Role = "user",
                    Content = "These are alternative locations: " + JsonConvert.SerializeObject(placeResult)
                }).Prepend(new Message()
                {
                    Role = "system",
                    Content = promot
                })
                .ToList();
            var commandOptions = new CommandOptions()
            {
                Messages = messages.ToArray()
            };
            
            var responseStream = await _openAIApiClient.SendStreamCommand(commandOptions);
            string completeMessage = string.Empty;

            using (var reader = new StreamReader(responseStream))
            {
                while (!reader.EndOfStream)
                {
                    string data = reader.ReadLine();

                    data = data.Replace("data: ", "").Trim();

                    if (string.IsNullOrWhiteSpace(data)) continue;

                    Completion obj = new Completion();
                    
                    if(data == "[DONE]")
                    {
                        obj.Choices = new Choice[]
                        {
                            new Choice()
                            {
                                FinishReason = "stop",
                            }   
                        };
                    }
                    else
                    {
                        obj = JsonConvert.DeserializeObject<Completion>(data);
                    }

                    data = string.Join("", obj.Choices?.Select(choice => choice.Delta?.Content));
                    data = data.Replace("\n", "");

                    await HttpContext.SSESendDataAsync(data);
                }
            }
        
        }

        [HttpPost]
        [Route("Command")]
        public async Task<ActionResult> Command([FromBody] MessageRequest messageRequest)
        {

            var apiKey = Request.Headers["Joi-ApiKey"];

            if (!string.IsNullOrEmpty(apiKey))
            {
                _openAiKey = apiKey;
            }

            var placeResult1 = await _gmpGisApiClient.NearPoint(messageRequest.Latitude, messageRequest.Longitude, 1000, "en-us", 20);

            var placeResult2 = await _arcGisApiClient.NearPoint(messageRequest.Latitude, messageRequest.Longitude, 1000, 20);

            var placeResult = new GmpGis.PlaceResult();
            placeResult.Places = placeResult1.Places.Union(placeResult2.Places).ToArray();

            var messages = messageRequest.Messages;
            var systemMessage = _promotTemplate.FirstOrDefault(x => x.Id == messageRequest.SystemId);

            var promot = systemMessage == null ? "You are ChatGPT, a large language model trained by OpenAI. Follow the user's instructions carefully. Respond using markdown format." : systemMessage.Promot;

            messages = messages.Prepend(new Message()
            {
                Role = "assistant",
                Content = "Sure, please provide further description."
            }).Prepend(new Message()
            {
                Role = "user",
                Content = "These are alternative locations: " + JsonConvert.SerializeObject(placeResult)
            }).Prepend(new Message()
            {
                Role = "system",
                Content = promot
            })
            .ToList();

            var completion = await _openAIApiClient.SendCommands(messages.ToArray(), messageRequest.Latitude, messageRequest.Longitude);

            return Ok(JsonConvert.SerializeObject(completion));

        }

        [HttpPost]
        [Route("Hint")]
        public async Task<ActionResult> Hint([FromBody] MessageRequest messageRequest)
        {
            var placeResult = await _gmpGisApiClient.NearPoint(messageRequest.Latitude, messageRequest.Longitude, 1000, "en-us", 20);

            var message = messageRequest.Messages.First();
            var systemMessage = _promotTemplate.First(template => template.Id == "gis_helper_3");

            var target = JsonConvert.DeserializeObject<Plan[]>(message.Content);

            var travelLocationJson = JsonConvert.SerializeObject(placeResult);

            systemMessage.Promot = systemMessage.Promot.Replace("__TRAVEL_LOCATIONS__", travelLocationJson);

            var hint = JsonConvert.SerializeObject(new Hint()
            {
                Plans = target
            });

            var messages = new Message[]
            {
                new Message()
                {
                    Role = "system",
                    Content = systemMessage.Promot.ToString()
                },
                new Message()
                {
                    Role = "user",
                    Content = hint
                }
            };

            var completion = await _openAIApiClient.SendCommands(messages.ToArray(), messageRequest.Latitude, messageRequest.Longitude);

            return Ok(JsonConvert.SerializeObject(completion));
        }

        [HttpPost]
        [Route("Post")]
        public async Task Post([FromBody] MessageRequest messageRequest)
        {
            await HttpContext.SSEInitAsync();

            var messages = messageRequest.Messages;
            var systemMessage = _promotTemplate.FirstOrDefault(x => x.Id == messageRequest.SystemId);

            messages = messages.Prepend(new Message()
            {
                Role = "system",
                Content = systemMessage == null ? "You are ChatGPT, a large language model trained by OpenAI. Follow the user's instructions carefully. Respond using markdown format." : systemMessage.Promot
            }).ToList();
            
            var responseStream = await _openAIApiClient.SendMessages(messages.ToArray());

            var tiktoken = TikToken.EncodingForModel("gpt-3.5-turbo");
            string completeMessage = string.Empty;

            using (var reader = new StreamReader(responseStream))
            {
                while (!reader.EndOfStream)
                {
                    string data = reader.ReadLine();

                    data = data.Replace("data: ", "").Trim();

                    if (string.IsNullOrWhiteSpace(data)) continue;

                    Completion obj = null;
                    
                    if(data == "[DONE]")
                    {
                        obj = new Completion()
                        {
                            Choices = new Choice[]
                            {
                                new Choice()
                                {
                                    FinishReason = "stop",
                                    TokenLength = tiktoken.Encode(completeMessage).Count
                                }
                            }
                        };
                    }
                    else
                    {
                        obj = JsonConvert.DeserializeObject<Completion>(data);

                        Array.ForEach(obj.Choices, (item) =>
                        {
                            if (string.IsNullOrWhiteSpace(item.Delta.Content)) return;
                            if (item.FinishReason == "stop")
                            {
                                item.TokenLength = tiktoken.Encode(completeMessage).Count;
                            }

                            completeMessage += item.Delta.Content;
                        });

                    }

                    var serializerSettings = new JsonSerializerSettings();
                    serializerSettings.ContractResolver = new CamelCasePropertyNamesContractResolver();
                    data = JsonConvert.SerializeObject(obj, serializerSettings);
                    
                    await HttpContext.SSESendDataAsync(data);
                }
            }

        }

    }

}