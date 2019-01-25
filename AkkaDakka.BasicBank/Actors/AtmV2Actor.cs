﻿ using System;
using Akka.Actor;
 using Akka.Cluster.Tools.PublishSubscribe;
 using AkkaBank.BasicBank.Messages.Account;
using AkkaBank.BasicBank.Messages.AtmV2;
using AkkaBank.BasicBank.Messages.Bank;
 using AkkaBank.BasicBank.Messages.BankAdmin;
 using AkkaBank.BasicBank.Messages.Console;

namespace AkkaBank.BasicBank.Actors
{
    public class AtmV2Actor : ReceiveActor
    {
        private IActorRef _console;
        private IActorRef _bank;
        private CustomerAccount _customerAccount;

        public AtmV2Actor()
        {
            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe("advert", Self));

            Become(WaitingForBankState);
        }
        
        protected override void PreStart()
        {
            _console = Context.ActorOf(Props.Create(() => new ConsoleActor()), "atm-console");
        }

        protected override void Unhandled(object message)
        {
            switch (message)
            {
                case ConsoleInputMessage cim:
                    _console.Tell("BEEP BEEP BEEP. UNEXPECTED CONSOLE INPUT!");
                    break;

                //possibly log other message types
            }            
        }

        #region States

        private void WaitingForBankState()
        {
            Receive<BankActorMessage>(HandleBankActor);
        }

        private void WaitingForCustomerNumberState()
        {
            Receive<ConsoleInputMessage>(HandleCustomerNumberInput);
            Receive<AdvertisementMessage>(HandleAdvertisement);
        }

        private void WaitingForCustomerState()
        {
            Receive<GetCustomerResponseMessage>(HandleCustomerResponse);
        }

        private void MainMenuState()
        {
            Receive<ConsoleInputMessage>(HandleMainMenuInput);
        }

        private void WithdrawalState()
        {
            Receive<ConsoleInputMessage>(HandleWithdrawalInput);            
        }

        private void DepositState()
        {
            Receive<ConsoleInputMessage>(HandleDepositInput);
        }

        private void WaitingForReceiptState()
        {
            Receive<ReceiptMessage>(HandleReceipt);
            Receive<ReceiptTimedOutMessage>(HandleTransactionTimedOut);
        }

        #endregion

        #region Handlers

        private void HandleBankActor(BankActorMessage message)
        {
            _bank = message.Bank;
            Become(WaitingForCustomerNumberState);
            _console.Tell(MakeWelcomeScreenMessage());
        }

        private void HandleAdvertisement(AdvertisementMessage message)
        {
            _console.Tell(MakeWelcomeScreenMessage(message));
        }

        private void HandleCustomerNumberInput(ConsoleInputMessage message)
        {
            if (int.TryParse(message.Input, out var accountNumber))
            {
                _bank.Tell(new GetCustomerRequstMessage(accountNumber));
                _console.Tell("Please wait.. taking to the bank.\n");
                Become(WaitingForCustomerState);
                return;
            }

            _console.Tell("That's not an account number! Try again:");
        }

        private void HandleTransactionTimedOut(ReceiptTimedOutMessage message)
        {
            _customerAccount = null;
            Become(WaitingForCustomerNumberState);
            _console.Tell(MakeWelcomeScreenMessage());
        }

        private void HandleCustomerResponse(GetCustomerResponseMessage message)
        {
            if (message.Ok)
            {
                _customerAccount = message.CustomerAccount;
                _console.Tell(MakeMainMenuScreenMessage());
                Become(MainMenuState);
                return;
            }

            _console.Tell("Unknown account!");
            Become(WaitingForCustomerNumberState);

            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromSeconds(7),
                _console,
                MakeWelcomeScreenMessage(),
                Self);
        }

        private void HandleMainMenuInput(ConsoleInputMessage message)
        {
            switch (message.Input)
            {
                case "w":
                    Become(WithdrawalState);
                    _console.Tell(
                        new ConsoleOutputMessage(
                            "****************************************\n" +
                            "*                                      *\n" +
                            "*                                      *\n" +
                            "*         WITHDRAWAL!!!.               *\n" +
                            "*                                      *\n" +
                            "*                                      *\n" +
                            "****************************************\n" +
                            "PLEASE ENTER AMOUNT:", true));
                    break;

                case "d":
                    Become(DepositState);
                    _console.Tell(
                        new ConsoleOutputMessage(
                            "****************************************\n" +
                            "*                                      *\n" +
                            "*                                      *\n" +
                            "*         DEPOSIT!!!.                  *\n" +
                            "*                                      *\n" +
                            "*                                      *\n" +
                            "****************************************\n" +
                            "PLEASE ENTER AMOUNT:", true));
                    break;

                default:
                    _console.Tell(MakeMainMenuScreenMessage());
                    _console.Tell("What!? Try again...");
                    break;
            }
        }

        private void HandleDepositInput(ConsoleInputMessage message)
        {
            if (int.TryParse(message.Input, out var amount))
            {
                _customerAccount.Account.Tell(new DepositMoneyMessage(amount));
                _console.Tell("Please wait.. taking to the bank.\n");
                Context.System.Scheduler.ScheduleTellOnce(
                    TimeSpan.FromSeconds(6),
                    Self,
                    new ReceiptTimedOutMessage(),
                    Self);

                Become(WaitingForReceiptState);
                return;
            }

            _console.Tell("That's not money! Try again:");
        }

        private void HandleWithdrawalInput(ConsoleInputMessage message)
        {
            if (int.TryParse(message.Input, out var amount))
            {
                _customerAccount.Account.Tell(new WithdrawMoneyMessage(amount));
                _console.Tell("Please wait.. taking to the bank.\n");
                Context.System.Scheduler.ScheduleTellOnce(
                    TimeSpan.FromSeconds(6),
                    Self,
                    new ReceiptTimedOutMessage(),
                    Self);

                Become(WaitingForReceiptState);
                return;
            }

            _console.Tell("That's not money! Try again:");
        }

        private void HandleReceipt(ReceiptMessage message)
        {
            _console.Tell("Your transaction is complete!...\n");
            _console.Tell($"The balance of your account is: ${message.Balance}\n");

            _customerAccount = null;
            Become(WaitingForCustomerNumberState);

            Context.System.Scheduler.ScheduleTellOnce(
                TimeSpan.FromSeconds(7),
                _console,
                MakeWelcomeScreenMessage(),                
                Self);
        }

        #endregion

        #region Screens        

        private ConsoleOutputMessage MakeMainMenuScreenMessage()
        {
            var name = _customerAccount.Customer.CustomerName;
            var MainMenuScreen =
                "****************************************\n" +
                "*                                      *\n" +
                "*                                      *\n" +
                $"*         Hi {name},         *\n" +
                "*         WELCOME TO BASIC BANK.       *\n" +
                "*                                      *\n" +
                "*         [w] WITHDRAWAL               *\n" +
                "*         [d] DEPOSIT                  *\n" +
                "*                                      *\n" +
                "*                                      *\n" +
                "*                                      *\n" +
                "****************************************\n";

            return new ConsoleOutputMessage(MainMenuScreen, true);            
        }

        private ConsoleOutputMessage MakeWelcomeScreenMessage(AdvertisementMessage advert = null)
        {
            var welcomeScreen =
                "****************************************\n" +
                "*                                      *\n" +
                "*                                      *\n" +
                "*         WELCOME TO BASIC BANK.       *\n";

            if (advert != null)
            {
                welcomeScreen += advert.Blurb;
            }

            welcomeScreen +=     "*                                      *\n" +
                "*         PLEASE ENTER YOU ACC.        *\n" +
                "*                                      *\n" +
                "*                                      *\n" +
                "*                                      *\n" +
                "****************************************\n";

            return new ConsoleOutputMessage(welcomeScreen, true);
        }

        #endregion
    }
}