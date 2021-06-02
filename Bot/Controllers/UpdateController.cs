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
        private readonly MessageService _messageService;

        public UpdateController(MessageService messageService)
        {
            _messageService = messageService;
        }

        [HttpPost]
        public async Task<IActionResult> ProcessUpdateAsync(Update update)
        {
            if (update.Type == UpdateType.Message)
            {
                await _messageService.HandleAsync(update.Message);
            }

            if (update.Type == UpdateType.ChannelPost)
            {
                await _messageService.HandleAsync(update.Message);
            }

            return Ok();
        }
    }
}
