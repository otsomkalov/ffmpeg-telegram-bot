using System;
using System.Threading.Tasks;
using Bot.Services;
using Microsoft.AspNetCore.Mvc;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Bot.Controllers
{
    [ApiController]
    [Route("update")]
    public class UpdateController : ControllerBase
    {
        private readonly IMessageService _messageService;

        public UpdateController(IMessageService messageService)
        {
            _messageService = messageService;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessUpdateAsync(Update update)
        {
            switch (update.Type)
            {
                case UpdateType.Message:

                    await _messageService.HandleAsync(update.Message);

                    break;
                
                case UpdateType.ChannelPost:

                    await _messageService.HandleAsync(update.ChannelPost);

                    break;
                case UpdateType.Unknown:
                case UpdateType.InlineQuery:
                case UpdateType.ChosenInlineResult:
                case UpdateType.CallbackQuery:
                case UpdateType.EditedMessage:
                case UpdateType.EditedChannelPost:
                case UpdateType.ShippingQuery:
                case UpdateType.PreCheckoutQuery:
                case UpdateType.Poll:
                case UpdateType.PollAnswer:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(update.Type));
            }

            return Ok();
        }
    }
}