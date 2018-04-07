﻿using System;
using System.Collections.Generic;
using System.Reactive.Linq;
using Acr;
using CoreBluetooth;
using Foundation;
#if __IOS__
using UIKit;
#endif


namespace Plugin.BluetoothLE
{
    public class GattCharacteristic : AbstractGattCharacteristic
    {
        readonly GattService serivceObj;
        public CBPeripheral Peripheral => this.serivceObj.Peripherial;
        public CBService NativeService => this.serivceObj.Service;
        public CBCharacteristic NativeCharacteristic { get; }


        public GattCharacteristic(GattService service, CBCharacteristic native) : base(service, native.UUID.ToGuid(), (CharacteristicProperties)(int)native.Properties)
        {
            this.serivceObj = service;
            this.NativeCharacteristic = native;
        }


        public override byte[] Value => this.NativeCharacteristic.Value?.ToArray();


        public override IObservable<CharacteristicGattResult> WriteWithoutResponse(byte[] value) => Observable.Create<CharacteristicGattResult>(ob =>
        {
            this.AssertWrite(false);

            void Send()
            {
                var data = NSData.FromArray(value);
                this.Peripheral.WriteValue(data, this.NativeCharacteristic, CBCharacteristicWriteType.WithoutResponse);
                var result = this.ToResult(GattEvent.Write, value);
                ob.Respond(result);
            }

#if __IOS__
            if (UIDevice.CurrentDevice.CheckSystemVersion(11, 0))
            {
                var handler = new EventHandler((sender, args) => Send());
                this.Peripheral.IsReadyToSendWriteWithoutResponse += handler;
                if (this.Peripheral.CanSendWriteWithoutResponse)
                    Send();
            }
            else
            {
                Send();
            }
            return () =>
            {

                if (UIDevice.CurrentDevice.CheckSystemVersion(11, 0))
                    this.Peripheral.IsReadyToSendWriteWithoutResponse -= handler;
            };
#else
            var handler = new EventHandler((sender, args) => Send());
            this.Peripheral.IsReadyToSendWriteWithoutResponse += handler;
            if (this.Peripheral.CanSendWriteWithoutResponse)
                Send();

            return () => this.Peripheral.IsReadyToSendWriteWithoutResponse -= handler;
#endif
        });


        public override IObservable<CharacteristicGattResult> Write(byte[] value) => Observable.Create<CharacteristicGattResult>(ob =>
        {
            this.AssertWrite(true);
            var data = NSData.FromArray(value);
            var handler = new EventHandler<CBCharacteristicEventArgs>((sender, args) =>
            {
                if (!this.Equals(args.Characteristic))
                    return;

                var result = args.Error == null
                    ? this.ToResult(GattEvent.Write, value)
                    : this.ToResult(GattEvent.WriteError, args.Error.Description);

                ob.Respond(result);
            });
            this.Peripheral.WroteCharacteristicValue += handler;
            this.Peripheral.WriteValue(data, this.NativeCharacteristic, CBCharacteristicWriteType.WithResponse);

            return () => this.Peripheral.WroteCharacteristicValue -= handler;
        });


        public override IObservable<CharacteristicGattResult> Read() => Observable.Create<CharacteristicGattResult>(ob =>
        {
            this.AssertRead();
            var handler = new EventHandler<CBCharacteristicEventArgs>((sender, args) =>
            {
                if (!this.Equals(args.Characteristic))
                    return;

                var result = args.Error == null
                    ? this.ToResult(GattEvent.Read, this.Value)
                    : this.ToResult(GattEvent.ReadError, args.Error.Description);

                ob.Respond(result);
            });
            this.Peripheral.UpdatedCharacterteristicValue += handler;
            this.Peripheral.ReadValue(this.NativeCharacteristic);

            return () => this.Peripheral.UpdatedCharacterteristicValue -= handler;
        });


        public override IObservable<CharacteristicGattResult> EnableNotifications(bool enableIndicationsIfAvailable)
        {
            this.AssertNotify();
            this.Peripheral.SetNotifyValue(true, this.NativeCharacteristic);
            return Observable.Return(this.ToResult(GattEvent.Notification, ""));
        }


        public override IObservable<CharacteristicGattResult> DisableNotifications()
        {
            this.AssertNotify();
            this.Peripheral.SetNotifyValue(false, this.NativeCharacteristic);
            return Observable.Return(this.ToResult(GattEvent.Notification, ""));
        }


        IObservable<CharacteristicGattResult> notifyOb;
        public override IObservable<CharacteristicGattResult> WhenNotificationReceived()
        {
            this.AssertNotify();

            this.notifyOb = this.notifyOb ?? Observable.Create<CharacteristicGattResult>(ob =>
            {
                var handler = new EventHandler<CBCharacteristicEventArgs>((sender, args) =>
                {
                    if (!this.Equals(args.Characteristic))
                        return;

                    var result = args.Error == null
                        ? this.ToResult(GattEvent.Notification, this.Value)
                        : this.ToResult(GattEvent.NotificationError, args.Error.Description);

                    ob.OnNext(result);
                });
                this.Peripheral.UpdatedCharacterteristicValue += handler;
                return () => this.Peripheral.UpdatedCharacterteristicValue -= handler;
            })
            .Publish()
            .RefCount();

            return this.notifyOb;
        }


        IObservable<IGattDescriptor> descriptorOb;
        public override IObservable<IGattDescriptor> DiscoverDescriptors()
        {
            this.descriptorOb = this.descriptorOb ?? Observable.Create<IGattDescriptor>(ob =>
            {
                var descriptors = new Dictionary<Guid, IGattDescriptor>();

                var handler = new EventHandler<CBCharacteristicEventArgs>((sender, args) =>
                {
                    if (this.NativeCharacteristic.Descriptors == null)
                        return;

                    foreach (var dnative in this.NativeCharacteristic.Descriptors)
                    {
                        var wrap = new GattDescriptor(this, dnative);
                        if (!descriptors.ContainsKey(wrap.Uuid))
                        {
                            descriptors.Add(wrap.Uuid, wrap);
                            ob.OnNext(wrap);
                        }
                    }
                });
                this.Peripheral.DiscoveredDescriptor += handler;
                this.Peripheral.DiscoverDescriptors(this.NativeCharacteristic);

                return () => this.Peripheral.DiscoveredDescriptor -= handler;
            })
            .Replay()
            .RefCount();

            return this.descriptorOb;
        }


        bool Equals(CBCharacteristic ch)
        {
            if (!this.NativeCharacteristic.UUID.Equals(ch.UUID))
                return false;

            if (!this.NativeService.UUID.Equals(ch.Service.UUID))
                return false;

			if (!this.Peripheral.Identifier.Equals(ch.Service.Peripheral.Identifier))
                return false;

            return true;
        }


        public override bool Equals(object obj)
        {
            var other = obj as GattCharacteristic;
            if (other == null)
                return false;

			if (!Object.ReferenceEquals(this, other))
                return false;

            return true;
        }


        public override int GetHashCode() => this.NativeCharacteristic.GetHashCode();
        public override string ToString() => this.Uuid.ToString();
    }
}