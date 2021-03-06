﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Akka.Actor;
using Akka.Cluster.Tools.PublishSubscribe;
using Akka.Routing;
using AkkaBank.BasicBank.Messages.Bank;

namespace AkkaBank.BasicBank.Actors
{
    public class BankV2Actor : ReceiveActor
    {
        private IActorRef _bankAccountsRouter;

        public BankV2Actor()
        {
            Receive<CreateCustomerRequest>(message =>
            {
                _bankAccountsRouter.Tell(message, Sender);
            });
            Receive<GetCustomerRequest>(message =>
            {
                _bankAccountsRouter.Tell(message, Sender);
            });
        }

        protected override void PreStart()
        {
            _bankAccountsRouter = Context.ActorOf(
                Props.Create<CustomerManagerV2Actor>().WithRouter(new ConsistentHashingPool(5)), "customer-manager-router");
        }
    }

    public class CustomerManagerV2Actor : ReceiveActor
    {      
        private readonly Dictionary<int, CustomerAccount> _customerAccounts = new Dictionary<int, CustomerAccount>();

        public CustomerManagerV2Actor()
        {
            Receive<CreateCustomerRequest>(HandleCreateCustomerRequest);
            Receive<GetCustomerRequest>(HandleGetCustomerRequest);
            Receive<GetCustomersRequest>(HandleGetCustomersRequest);

            var mediator = DistributedPubSub.Get(Context.System).Mediator;
            mediator.Tell(new Subscribe("request-customer-accounts", Self));
        }

        protected override SupervisorStrategy SupervisorStrategy()
        {
            return new OneForOneStrategy(
                maxNrOfRetries: 10,
                withinTimeRange: TimeSpan.FromMinutes(1),
                localOnlyDecider: ex =>
                {
                    switch (ex)
                    {
                        case NegativeAccountBalanceException nabe:
                            return Directive.Resume;
                        default:
                            return Directive.Escalate;
                    }
                });
        }

        private void HandleCreateCustomerRequest(CreateCustomerRequest message)
        {
            GetCustomerResponse response;

            if (_customerAccounts.ContainsKey(message.Customer.CustomerNumber))
            {
                response = new GetCustomerResponse("The account already exists.");
            }
            else
            {
                var account = Context.ActorOf(Props.Create(() => new AccountActor()), $"account-{message.Customer.CustomerNumber}");
                var customerAccount = new CustomerAccount(message.Customer, account);
                _customerAccounts.Add(message.Customer.CustomerNumber, customerAccount);
                response = new GetCustomerResponse(customerAccount);
            }

            if (!Sender.IsNobody())
            {
                Sender.Tell(response);
            }            
        }

        private void HandleGetCustomerRequest(GetCustomerRequest message)
        {
            //Pretend that it takes some time to find an account.
            Task.Delay(2000).GetAwaiter().GetResult();

            if (_customerAccounts.TryGetValue(message.CustomerNumber, out var customerAccount))
            {
                Sender.Tell(new GetCustomerResponse(customerAccount));
                return;
            }

            Sender.Tell(new GetCustomerResponse("No account found."));
        }

        private void HandleGetCustomersRequest(GetCustomersRequest message)
        {
            //If we were better citizens we would spawn this to a child actor
            foreach (var customerAccount in _customerAccounts)
            {
                //Pretend that it takes some time to find an account.                
                Task.Delay(2000).GetAwaiter().GetResult();

                Sender.Tell(new GetCustomerResponse(customerAccount.Value));
            }
        }
    }
}