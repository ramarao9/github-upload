﻿using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vij.Bots.DynamicsCRMBot.Helpers;
using Vij.Bots.DynamicsCRMBot.Interfaces;
using Vij.Bots.DynamicsCRMBot.Models;
using Vij.Bots.DynamicsCRMBot.Services;

namespace Vij.Bots.DynamicsCRMBot.Dialogs
{
    public class MainDialog : ComponentDialog
    {

        CDSRecognizer _luisRecognizer;
        private readonly StateService _stateService;
        protected readonly ILogger _logger;

        ICaseRepository _subjectRepository;
        IContactRepository _contactRepository;
        IInvoiceRepository _invoiceRepository;
        IConfiguration _configuration;


        AIFormRecognizer _aiFormRecognizer;

        public MainDialog(StateService stateService, CDSRecognizer luisRecognizer,
            ILogger<MainDialog> logger, ICaseRepository subjectRepository,
            IContactRepository contactRepository, IInvoiceRepository invoiceRepository, IConfiguration configuration) : base(nameof(MainDialog))
        {
            _logger = logger;
            _luisRecognizer = luisRecognizer;
            _stateService = stateService;
            _subjectRepository = subjectRepository;
            _contactRepository = contactRepository;
            _invoiceRepository = invoiceRepository;

            var waterfallSteps = new WaterfallStep[]
            {
                InitialStepAsync,
                FinalStepAsync
            };

            _aiFormRecognizer = new AIFormRecognizer(configuration);


            AddDialog(new GreetingDialog($"{nameof(MainDialog)}.greeting", _stateService));
            // AddDialog(new KBDialog());
            AddDialog(new NewCaseDialog($"{nameof(MainDialog)}.newCase", _stateService, _subjectRepository, contactRepository));

            AddDialog(new InvoiceDialog($"{nameof(MainDialog)}.invoiceUpload", _stateService, _contactRepository, _invoiceRepository, _aiFormRecognizer));
            //  AddDialog(new AppointmentDialog());

            AddDialog(new WaterfallDialog($"{nameof(MainDialog)}.mainFlow", waterfallSteps));

            // The initial child Dialog to run.
            InitialDialogId = $"{nameof(MainDialog)}.mainFlow";

        }


        private async Task<DialogTurnResult> InitialStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // First, we use the dispatch model to determine which cognitive service (LUIS or QnA) to use.
            var recognizerResult = await _luisRecognizer.RecognizeAsync(stepContext.Context, cancellationToken);
            ConversationData conversationData = await _stateService.ConversationDataAccessor.GetAsync(stepContext.Context, () => new ConversationData());

            string text = stepContext.Context.Activity.Text;

            bool dialogCompleted = stepContext.Context.TurnState.ContainsKey("DialogCompleted");

            var topIntent = recognizerResult.GetTopScoringIntent();

            if (dialogCompleted)
            {
                stepContext.Context.TurnState.Remove("DialogCompleted");
            }
            else
            {

                Dictionary<string, object> contextVars = new Dictionary<string, object>() { { "BotHandoffTopic", "CreditCard" } };

                switch (topIntent.intent.ToLower())
                {
                    case "greetingintent":
                        return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.greeting", null, cancellationToken);

                    case "humaninteraction":
                        OmnichannelBotClient.AddEscalationContext(stepContext.Context.Activity, contextVars);
                        await stepContext.Context.SendActivityAsync(MessageFactory.Text("requestescalation"), cancellationToken);
                        break;

                    case "issue":
                        return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.newCase", null, cancellationToken);


                    case "invoiceupload":
                        return await stepContext.BeginDialogAsync($"{nameof(MainDialog)}.invoiceUpload", null, cancellationToken);

                    case "thank you":
                        await stepContext.Context.SendActivityAsync(MessageFactory.Text("You are welcome"), cancellationToken);
                        break;

                    case "none":
                        break;

                    default:
                        await stepContext.Context.SendActivityAsync(MessageFactory.Text($"I'm sorry I don't know what you mean."), cancellationToken);
                        break;
                }


            }


            return await stepContext.NextAsync(null, cancellationToken);

        }





        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {

            bool dialogCompleted = stepContext.Context.TurnState.ContainsKey("DialogCompleted");

            if (dialogCompleted)
            {
                // Restart the main dialog with a different message the second time around
                var promptMessage = "Is there anything else I can help you with?";
                await stepContext.Context.SendActivityAsync(MessageFactory.Text(promptMessage), cancellationToken);
                return await stepContext.ReplaceDialogAsync(InitialDialogId, null, cancellationToken);
            }
            else
            {
                return await stepContext.EndDialogAsync(null, cancellationToken);
            }

        }
    }
}
