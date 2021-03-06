//
// System.Security.Cryptography.X509Certificates.X509Chain
//
// Author:
//	Sebastien Pouliot  <sebastien@ximian.com>
//
// (C) 2003 Motus Technologies Inc. (http://www.motus.com)
// Copyright (C) 2004-2006 Novell Inc. (http://www.novell.com)
// Copyright (C) 2011 Xamarin Inc. (http://www.xamarin.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Utilities.Encoders;
using Org.BouncyCastle.X509;
#if SECURITY_DEP

#if MONOTOUCH || MONODROID || SSHARP
#if MONO
using MX = Mono.Security.X509;
#endif
#else
extern alias MonoSecurity;
using MX = MonoSecurity::Mono.Security.X509;
#endif

#if SSHARP
#if MONO
using SSMono;
#endif
using System.Linq;
using CCrypto = Crestron.SimplSharp.Cryptography;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Security.Certificates;
using X509Certificate = Org.BouncyCastle.X509.X509Certificate;
using X509Certificate2 = Org.BouncyCastle.X509.X509Certificate;
using BCX509Certificate2 = Org.BouncyCastle.X509.X509Certificate;
using X500DistinguishedName = Org.BouncyCastle.Asn1.X509.X509Name;
using AsymmetricAlgorithm = Org.BouncyCastle.Crypto.AsymmetricKeyParameter;
using Oid = Org.BouncyCastle.Asn1.DerObjectIdentifier;
#else
#endif

using System;
using System.Collections;
using System.Text;

#if SSHARP
namespace Crestron.SimplSharp.Cryptography.X509Certificates
#else
namespace System.Security.Cryptography.X509Certificates
#endif
	{
	public class X509Chain
		{
		private StoreLocation location;
		private X509ChainElementCollection elements;
		private X509ChainPolicy policy;
		private X509ChainStatus[] status;

		private static X509ChainStatus[] Empty = new X509ChainStatus[0];

		// RFC3280 variables
		private int max_path_length;
		private X500DistinguishedName working_issuer_name;
		private AsymmetricAlgorithm working_public_key;

		// other flags
		private X509ChainElement bce_restriction;

		// constructors

		public X509Chain ()
			: this (false)
			{
			}

		public X509Chain (bool useMachineContext)
			{
			location = useMachineContext ? StoreLocation.LocalMachine : StoreLocation.CurrentUser;
			elements = new X509ChainElementCollection ();
			policy = new X509ChainPolicy ();
			}

		public X509Chain (IntPtr chainContext)
			{
			// CryptoAPI compatibility (unmanaged handle)
			throw new NotSupportedException ();
			}

		// properties

		public IntPtr ChainContext
			{
			get { return IntPtr.Zero; }
			}

		public X509ChainElementCollection ChainElements
			{
			get { return elements; }
			}

		public X509ChainPolicy ChainPolicy
			{
			get { return policy; }
			set { policy = value; }
			}

		public X509ChainStatus[] ChainStatus
			{
			get
				{
				if (status == null)
					return Empty;
				return status;
				}
			}

		// methods

		public bool Build (BCX509Certificate2 certificate)
			{
			if (certificate == null)
				throw new ArgumentException ("certificate");

			Reset ();
			X509ChainStatusFlags flag;
			try
				{
				flag = BuildChainFrom (certificate);
				ValidateChain (flag);
				}
			catch (CryptographicException ce)
				{
				throw new ArgumentException ("certificate", ce);
				}

			X509ChainStatusFlags total = X509ChainStatusFlags.NoError;
			ArrayList list = new ArrayList ();
			// build "global" ChainStatus from the ChainStatus of every ChainElements
			foreach (X509ChainElement ce in elements)
				{
				foreach (X509ChainStatus cs in ce.ChainElementStatus)
					{
					// we MUST avoid duplicates in the "global" list
					if ((total & cs.Status) != cs.Status)
						{
						list.Add (cs);
						total |= cs.Status;
						}
					}
				}
			// and if required add some
			if (flag != X509ChainStatusFlags.NoError)
				list.Insert (0, new X509ChainStatus (flag));
			status = (X509ChainStatus[])list.ToArray (typeof(X509ChainStatus));

			// (fast path) this ignore everything we have checked
			if ((status.Length == 0) || (ChainPolicy.VerificationFlags == X509VerificationFlags.AllFlags))
				return true;

			bool result = true;
			// now check if exclude some verification for the "end result" (boolean)
			foreach (X509ChainStatus cs in status)
				{
				switch (cs.Status)
					{
					case X509ChainStatusFlags.UntrustedRoot:
					case X509ChainStatusFlags.PartialChain:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.AllowUnknownCertificateAuthority) != 0);
						break;
					case X509ChainStatusFlags.NotTimeValid:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreNotTimeValid) != 0);
						break;
						// FIXME - from here we needs new test cases for all cases
					case X509ChainStatusFlags.NotTimeNested:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreNotTimeNested) != 0);
						break;
					case X509ChainStatusFlags.InvalidBasicConstraints:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreInvalidBasicConstraints) != 0);
						break;
					case X509ChainStatusFlags.InvalidPolicyConstraints:
					case X509ChainStatusFlags.NoIssuanceChainPolicy:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreInvalidPolicy) != 0);
						break;
					case X509ChainStatusFlags.InvalidNameConstraints:
					case X509ChainStatusFlags.HasNotSupportedNameConstraint:
					case X509ChainStatusFlags.HasNotPermittedNameConstraint:
					case X509ChainStatusFlags.HasExcludedNameConstraint:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreInvalidName) != 0);
						break;
					case X509ChainStatusFlags.InvalidExtension:
						// not sure ?!?
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreWrongUsage) != 0);
						break;
						//
						//	((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreRootRevocationUnknown) != 0)
						//	((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreEndRevocationUnknown) != 0)
					case X509ChainStatusFlags.CtlNotTimeValid:
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreCtlNotTimeValid) != 0);
						break;
					case X509ChainStatusFlags.CtlNotSignatureValid:
						// ?
						break;
						//	((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreCtlSignerRevocationUnknown) != 0);
					case X509ChainStatusFlags.CtlNotValidForUsage:
						// FIXME - does IgnoreWrongUsage apply to CTL (it doesn't have Ctl in it's name like the others)
						result &= ((ChainPolicy.VerificationFlags & X509VerificationFlags.IgnoreWrongUsage) != 0);
						break;
					default:
						result = false;
						break;
					}
				// once we have one failure there's no need to check further
				if (!result)
					return false;
				}

			// every "problem" was excluded
			return true;
			}

		public void Reset ()
			{
			// note: this call doesn't Reset the X509ChainPolicy
			if ((status != null) && (status.Length != 0))
				status = null;
			if (elements.Count > 0)
				elements.Clear ();
			if (user_root_store != null)
				{
				user_root_store.Close ();
				user_root_store = null;
				}
			if (root_store != null)
				{
				root_store.Close ();
				root_store = null;
				}
			if (user_ca_store != null)
				{
				user_ca_store.Close ();
				user_ca_store = null;
				}
			if (ca_store != null)
				{
				ca_store.Close ();
				ca_store = null;
				}
			roots = null;
			cas = null;
			collection = null;
			bce_restriction = null;
			working_public_key = null;
			}

		// static methods

		public static X509Chain Create ()
			{
#if FULL_AOT_RUNTIME || SSHARP
			return new X509Chain ();
#else
			return (X509Chain)CryptoConfig.CreateFromName ("X509Chain");
#endif
			}

		// private stuff

		private BCX509CertificateCollection roots;
		private BCX509CertificateCollection cas;
		private X509Store root_store;
		private X509Store ca_store;
		private X509Store user_root_store;
		private X509Store user_ca_store;

		private BCX509CertificateCollection Roots
			{
			get
				{
				if (roots == null)
					{
					BCX509CertificateCollection c = new BCX509CertificateCollection ();
					X509Store store = LMRootStore;
					if (location == StoreLocation.CurrentUser)
						c.AddRange (UserRootStore.Certificates);
					c.AddRange (store.Certificates);
					roots = c;
					}
				return roots;
				}
			}

		private BCX509CertificateCollection CertificateAuthorities
			{
			get
				{
				if (cas == null)
					{
					BCX509CertificateCollection c = new BCX509CertificateCollection ();
					X509Store store = LMCAStore;
					if (location == StoreLocation.CurrentUser)
						c.AddRange (UserCAStore.Certificates);
					c.AddRange (store.Certificates);
					cas = c;
					}
				return cas;
				}
			}

		private X509Store LMRootStore
			{
			get
				{
				if (root_store == null)
					{
					root_store = new X509Store (StoreName.Root, StoreLocation.LocalMachine);
					try
						{
						root_store.Open (/*OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly*/);
						}
					catch
						{
						}
					}
				return root_store;
				}
			}

		private X509Store UserRootStore
			{
			get
				{
				if (user_root_store == null)
					{
					user_root_store = new X509Store (StoreName.Root, StoreLocation.CurrentUser);
					try
						{
						user_root_store.Open (/*OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly*/);
						}
					catch
						{
						}
					}
				return user_root_store;
				}
			}

		private X509Store LMCAStore
			{
			get
				{
				if (ca_store == null)
					{
					ca_store = new X509Store (StoreName.CertificateAuthority, StoreLocation.LocalMachine);
					try
						{
						ca_store.Open (/*OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly*/);
						}
					catch
						{
						}
					}
				return ca_store;
				}
			}

		private X509Store UserCAStore
			{
			get
				{
				if (user_ca_store == null)
					{
					user_ca_store = new X509Store (StoreName.CertificateAuthority, StoreLocation.CurrentUser);
					try
						{
						user_ca_store.Open (/*OpenFlags.OpenExistingOnly | OpenFlags.ReadOnly*/);
						}
					catch
						{
						}
					}
				return user_ca_store;
				}
			}

		// *** certificate chain/path building stuff ***

		private BCX509CertificateCollection collection;

		// we search local user (default) or machine certificate store 
		// and in the extra certificate supplied in ChainPolicy.ExtraStore
		private BCX509CertificateCollection CertificateCollection
			{
			get
				{
				if (collection == null)
					{
					collection = new BCX509CertificateCollection (ChainPolicy.ExtraStore);
					collection.AddRange (Roots);
					collection.AddRange (CertificateAuthorities);
					}
				return collection;
				}
			}

		// This is a non-recursive chain/path building algorithm. 
		//
		// At this stage we only checks for PartialChain, Cyclic and UntrustedRoot errors are they
		// affect the path building (other errors are verification errors).
		//
		// Note that the order match the one we need to match MS and not the one defined in RFC3280,
		// we also include the trusted root certificate (trust anchor in RFC3280) in the list.
		// (this isn't an issue, just keep that in mind if you look at the source and the RFC)
		private X509ChainStatusFlags BuildChainFrom (BCX509Certificate2 certificate)
			{
			elements.Add (certificate);

			while (!IsChainComplete (certificate))
				{
				certificate = FindParent (certificate);

				if (certificate == null)
					return X509ChainStatusFlags.PartialChain;

				if (elements.Contains (certificate))
					return X509ChainStatusFlags.Cyclic;

				elements.Add (certificate);
				}

			// roots may be supplied (e.g. in the ExtraStore) so we need to confirm their
			// trustiness (what a cute word) in the trusted root collection
			if (!Roots.Contains (certificate))
				elements[elements.Count - 1].StatusFlags |= X509ChainStatusFlags.UntrustedRoot;

			return X509ChainStatusFlags.NoError;
			}

		private BCX509Certificate2 SelectBestFromCollection (BCX509Certificate2 child, BCX509CertificateCollection c)
			{
			switch (c.Count)
				{
				case 0:
					return null;
				case 1:
					return c[0];
				default:
					// multiple candidate, keep only the ones that are still valid
					BCX509CertificateCollection time_valid = c.Find (BCX509FindType.FindByTimeValid, ChainPolicy.VerificationTime, false);
					switch (time_valid.Count)
						{
						case 0:
							// that's too restrictive, let's revert and try another thing...
							time_valid = c;
							break;
						case 1:
							return time_valid[0];
						default:
							break;
						}

					// again multiple candidates, let's find the AKI that match the SKI (if we have one)
					string aki = GetAuthorityKeyIdentifier (child);
					if (String.IsNullOrEmpty (aki))
						return time_valid[0]; // FIXME: out of luck, you get the first one
					foreach (BCX509Certificate2 parent in time_valid)
						{
						string ski = GetSubjectKeyIdentifier (parent);
						// if both id are available then they must match
						if (aki == ski)
							return parent;
						}
					return time_valid[0]; // FIXME: out of luck, you get the first one
				}
			}

		private BCX509Certificate2 FindParent (BCX509Certificate2 certificate)
			{
			BCX509CertificateCollection subset = CertificateCollection.Find (BCX509FindType.FindBySubjectDistinguishedName, certificate.IssuerDN, false);
			string aki = GetAuthorityKeyIdentifier (certificate);
			if ((aki != null) && (aki.Length > 0))
				subset.AddRange (CertificateCollection.Find (BCX509FindType.FindBySubjectKeyIdentifier, aki, false));
			BCX509Certificate2 parent = SelectBestFromCollection (certificate, subset);
			// if parent==certificate we're looping but it's not (probably) a bug and not a true cyclic (over n certs)
			return certificate.Equals (parent) ? null : parent;
			}

		private bool IsChainComplete (BCX509Certificate2 certificate)
			{
			// the chain is complete if we have a self-signed certificate
			if (!IsSelfIssued (certificate))
				return false;

			// we're very limited to what we can do without certificate extensions
			//if (certificate.Version < 3)
			//	return true;

			// check that Authority Key Identifier == Subject Key Identifier
			// e.g. it will be different if a self-signed certificate is part (not the end) of the chain
			string ski = GetSubjectKeyIdentifier (certificate);
			if (String.IsNullOrEmpty (ski))
				return true;
			string aki = GetAuthorityKeyIdentifier (certificate);
			if (String.IsNullOrEmpty (aki))
				return true;
			// if both id are available then they must match
			return (aki == ski);
			}

		// check for "self-issued" certificate - without verifying the signature
		// note that self-issued doesn't always mean it's a root certificate!
		private bool IsSelfIssued (BCX509Certificate2 certificate)
			{
			return (certificate.IssuerDN.Equals(certificate.SubjectDN));
			}

		// *** certificate chain/path validation stuff ***

		// Currently a subset of RFC3280 (hopefully a full implementation someday)
		private void ValidateChain (X509ChainStatusFlags flag)
			{
			// 'n' should be the root certificate... 
			int n = elements.Count - 1;
			BCX509Certificate2 certificate = elements[n].BCCertificate;

			// ... and, if so, must be treated outside the chain... 
			if (((flag & X509ChainStatusFlags.PartialChain) == 0))
				{
				Process (n);
				// deal with the case where the chain == the root certificate 
				// (which isn't for RFC3280) part of the chain
				if (n == 0)
					{
					elements[0].UncompressFlags ();
					return;
					}
				// skip the root certificate when processing the chain (in 6.1.3)
				n--;
				}
			// ... unless the chain is a partial one (then we start with that one)

			// 6.1.1 - Inputs
			// 6.1.1.a - a prospective certificate path of length n (i.e. elements)
			// 6.1.1.b - the current date/time (i.e. ChainPolicy.VerificationTime)
			// 6.1.1.c - user-initial-policy-set (i.e. ChainPolicy.CertificatePolicy)
			// 6.1.1.d - the trust anchor information (i.e. certificate, unless it's a partial chain)
			// 6.1.1.e - initial-policy-mapping-inhibit (NOT SUPPORTED BY THE API)
			// 6.1.1.f - initial-explicit-policy (NOT SUPPORTED BY THE API)
			// 6.1.1.g - initial-any-policy-inhibit (NOT SUPPORTED BY THE API)

			// 6.1.2 - Initialization (incomplete)
			// 6.1.2.a-f - policy stuff, some TODO, some not supported
			// 6.1.2.g - working public key algorithm
//			working_public_key_algorithm = certificate.PublicKey.Oid.Value;
			// 6.1.2.h-i - our key contains both the "working public key" and "working public key parameters" data
			working_public_key = certificate.GetPublicKey ();
			// 6.1.2.j - working issuer name
			working_issuer_name = certificate.IssuerDN;
			// 6.1.2.k - this integer is initialized to n, is decremented for each non-self-issued, certificate and
			//	     may be reduced to the value in the path length constraint field
			max_path_length = n;

			// 6.1.3 - Basic Certificate Processing
			// note: loop looks reversed (the list is) but we process this part just like RFC3280 does
			for (int i = n; i > 0; i--)
				{
				Process (i);
				// 6.1.4 - preparation for certificate i+1 (for not with i+1, or i-1 in our loop)
				PrepareForNextCertificate (i);
				}
			Process (0);

			// 6.1.3.a.3 - revocation checks
			CheckRevocationOnChain (flag);

			// 6.1.5 - Wrap-up procedure
			WrapUp ();
			}

		private void Process (int n)
			{
			X509ChainElement element = elements[n];
			BCX509Certificate2 certificate = element.BCCertificate;

			// pre-step: DSA certificates may inherit the parameters of their CA
			if ((n != elements.Count - 1) && (certificate.SigAlgName == "1.2.840.10040.4.1"))
				{
				if (certificate.GetPublicKey() == null)
					{
					BCX509Certificate2 parent = elements[n + 1].BCCertificate;
					//certificate.GetPublicKey() = parent.GetPublicKey ();
					}
				}

			bool root = (working_public_key == null);
			// 6.1.3.a.1 - check signature (with special case to deal with root certificates)
			if (!IsSignedWith (certificate, root ? certificate.GetPublicKey () : working_public_key))
				{
				// another special case where only an end-entity is available and can't be verified.
				// In this case we do not report an invalid signature (since this is unknown)
				if (root || (n != elements.Count - 1) || IsSelfIssued (certificate))
					element.StatusFlags |= X509ChainStatusFlags.NotSignatureValid;
				}

			// 6.1.3.a.2 - check validity period
			if ((ChainPolicy.VerificationTime < certificate.NotBefore) || (ChainPolicy.VerificationTime > certificate.NotAfter))
				element.StatusFlags |= X509ChainStatusFlags.NotTimeValid;
			// TODO - for X509ChainStatusFlags.NotTimeNested (needs global structure)

			// note: most of them don't apply to the root certificate
			if (root)
				return;

			// 6.1.3.a.3 - revocation check (we're doing at the last stage)
			// note: you revoke a trusted root by removing it from your trusted store (i.e. no CRL can do this job)

			// 6.1.3.a.4 - check certificate issuer name
			if (!certificate.IssuerDN.Equals(working_issuer_name))
				{
				// NOTE: this is not the "right" error flag, but it's the closest one defined
				element.StatusFlags |= X509ChainStatusFlags.InvalidNameConstraints;
				}

			if (!IsSelfIssued (certificate) && (n != 0))
				{
				// TODO 6.1.3.b - subject name in the permitted_subtrees ...
				// TODO 6.1.3.c - subject name not within excluded_subtrees...

				// TODO - check for X509ChainStatusFlags.InvalidNameConstraint
				// TODO - check for X509ChainStatusFlags.HasNotSupportedNameConstraint
				// TODO - check for X509ChainStatusFlags.HasNotPermittedNameConstraint
				// TODO - check for X509ChainStatusFlags.HasExcludedNameConstraint
				}

			// TODO 6.1.3.d - check if certificate policies extension is present
			//if (false) {
			// TODO - for X509ChainStatusFlags.InvalidPolicyConstraints
			//	using X509ChainPolicy.ApplicationPolicy and X509ChainPolicy.CertificatePolicy

			// TODO - check for X509ChainStatusFlags.NoIssuanceChainPolicy

			//} else {
			// TODO 6.1.3.e - set valid_policy_tree to NULL
			//}

			// TODO 6.1.3.f - verify explict_policy > 0 if valid_policy_tree != NULL
			}

		// CTL == Certificate Trust List / NOT SUPPORTED
		// TODO - check for X509ChainStatusFlags.CtlNotTimeValid
		// TODO - check for X509ChainStatusFlags.CtlNotSignatureValid
		// TODO - check for X509ChainStatusFlags.CtlNotValidForUsage

		private void PrepareForNextCertificate (int n)
			{
			X509ChainElement element = elements[n];
			BCX509Certificate2 certificate = element.BCCertificate;

			// TODO 6.1.4.a-b

			// 6.1.4.c
			working_issuer_name = certificate.SubjectDN;
			// 6.1.4.d-e - our key includes both the public key and it's parameters
			working_public_key = certificate.GetPublicKey();
			// 6.1.4.f
//			working_public_key_algorithm = certificate.PublicKey.Oid.Value;

			// TODO 6.1.4.g-j

			// 6.1.4.k - Verify that the certificate is a CA certificate
			//BasicConstraints bce = (certificate.Extensions["2.5.29.19"] as BasicConstraints);
			BasicConstraints bce = BasicConstraints.GetInstance (certificate.CertificateStructure.TbsCertificate.Extensions.GetExtension (X509Extensions.BasicConstraints));
			if (bce != null)
				{
				if (!bce.IsCA ())
					element.StatusFlags |= X509ChainStatusFlags.InvalidBasicConstraints;
				}
			else if (certificate.Version >= 3)
				{
				// recent (v3+) CA certificates must include BCE
				element.StatusFlags |= X509ChainStatusFlags.InvalidBasicConstraints;
				}

			// 6.1.4.l - if the certificate isn't self-issued...
			if (!IsSelfIssued (certificate))
				{
				// ... verify that max_path_length > 0
				if (max_path_length > 0)
					max_path_length--;
				else
					{
					// to match MS the reported status must be against the certificate 
					// with the BCE and not where the path is too long. It also means
					// that this condition has to be reported only once
					if (bce_restriction != null)
						bce_restriction.StatusFlags |= X509ChainStatusFlags.InvalidBasicConstraints;
					}
				}

			// 6.1.4.m - if pathLengthConstraint is present...
			if ((bce != null) && (bce.PathLenConstraint!= null))
				{
				// ... and is less that max_path_length, set max_path_length to it's value
				if (bce.PathLenConstraint.IntValue < max_path_length)
					{
					max_path_length = bce.PathLenConstraint.IntValue;
					bce_restriction = element;
					}
				}

			// 6.1.4.n - if key usage extension is present...
			//X509KeyUsage kue = (certificate.Extensions["2.5.29.15"] as X509KeyUsage);
			KeyUsage kue = KeyUsage.GetInstance (certificate.CertificateStructure.TbsCertificate.Extensions.GetExtension (X509Extensions.KeyUsage));
			if (kue != null)
				{
				// ... verify keyCertSign is set
				//KeyUsage success = new KeyUsage(KeyUsage.KeyCertSign);
				//if ((kue.KeyUsages & success) != success)
				if ((kue.IntValue & KeyUsage.KeyCertSign) != KeyUsage.KeyCertSign)
					element.StatusFlags |= X509ChainStatusFlags.NotValidForUsage;
				}

			// 6.1.4.o - recognize and process other critical extension present in the certificate
			ProcessCertificateExtensions (element);
			}

		private void WrapUp ()
			{
			X509ChainElement element = elements[0];
			BCX509Certificate2 certificate = element.BCCertificate;

			// 6.1.5.a - TODO if certificate n (our 0) wasn't self issued and explicit_policy != 0
			if (IsSelfIssued (certificate))
				{
				// TODO... decrement explicit_policy by 1
				}

			// 6.1.5.b - TODO

			// 6.1.5.c,d,e - not required by the X509Chain implementation

			// 6.1.5.f - recognize and process other critical extension present in the certificate
			ProcessCertificateExtensions (element);

			// 6.1.5.g - TODO

			// uncompressed the flags into several elements
			for (int i = elements.Count - 1; i >= 0; i--)
				elements[i].UncompressFlags ();
			}

		private void ProcessCertificateExtensions (X509ChainElement element)
			{
			var exts = element.BCCertificate.CertificateStructure.TbsCertificate.Extensions;
			foreach (var extOid in exts.GetExtensionOids ())
				{
				var ext = exts.GetExtension (extOid);
				if (ext.IsCritical)
					{
					switch (extOid.Id)
						{
						case "2.5.29.15": // X509KeyUsageExtension
						case "2.5.29.19": // X509BasicConstraintsExtension
							// we processed this extension
							break;
						default:
							// note: Under Windows XP MS implementation seems to ignore 
							// certificate with unknown critical extensions.
							element.StatusFlags |= X509ChainStatusFlags.InvalidExtension;
							break;
						}
					}
				}
			}

		private bool IsSignedWith (BCX509Certificate2 signed, AsymmetricAlgorithm pubkey)
			{
			if (pubkey == null)
				return false;

			try
				{
				signed.Verify (pubkey);
				return true;
				}
			catch (Exception)
				{
				return false;
				}
			}

		private string GetSubjectKeyIdentifier (BCX509Certificate2 certificate)
			{
			//SubjectKeyIdentifier ski = (certificate.Extensions["2.5.29.14"] as SubjectKeyIdentifier);
			SubjectKeyIdentifier ski = SubjectKeyIdentifier.GetInstance (certificate.CertificateStructure.TbsCertificate.Extensions.GetExtension (X509Extensions.SubjectKeyIdentifier));
			return (ski == null) ? String.Empty : Hex.ToHexString(ski.GetKeyIdentifier ());
			}

		// System.dll v2 doesn't have a class to deal with the AuthorityKeyIdentifier extension
		private static string GetAuthorityKeyIdentifier (BCX509Certificate2 certificate)
			{
			//return GetAuthorityKeyIdentifier (certificate.Extensions["2.5.29.35"]);
			//return GetAuthorityKeyIdentifier (certificate.CertificateStructure.TbsCertificate.Extensions.GetExtension (new Oid ("2.5.29.35")).GetParsedValue () as AuthorityKeyIdentifier);
			AuthorityKeyIdentifier ski = AuthorityKeyIdentifier.GetInstance(certificate.CertificateStructure.TbsCertificate.Extensions.GetExtension (X509Extensions.AuthorityKeyIdentifier));
			return (ski == null) ? String.Empty : Hex.ToHexString(ski.GetKeyIdentifier ());
			}

#if !NETCF
		// but anyway System.dll v2 doesn't expose CRL in any way so...
		private static string GetAuthorityKeyIdentifier (X509Crl crl)
			{
			return GetAuthorityKeyIdentifier (crl.Extensions["2.5.29.35"]);
			}

		private static string GetAuthorityKeyIdentifier (X509Extension ext)
			{
			if (ext == null)
				return String.Empty;
			AuthorityKeyIdentifier aki = new AuthorityKeyIdentifier (ext);
			byte[] id = aki.Identifier;
			if (id == null)
				return String.Empty;
			StringBuilder sb = new StringBuilder ();
			foreach (byte b in id)
				sb.Append (b.ToString ("X02"));
			return sb.ToString ();
			}
#endif

		// we check the revocation only once we have built the complete chain
		private void CheckRevocationOnChain (X509ChainStatusFlags flag)
			{
			bool partial = ((flag & X509ChainStatusFlags.PartialChain) != 0);
			bool online;

			switch (ChainPolicy.RevocationMode)
				{
				case X509RevocationMode.Online:
					// default
					online = true;
					break;
				case X509RevocationMode.Offline:
					online = false;
					break;
				case X509RevocationMode.NoCheck:
					return;
				default:
					throw new InvalidOperationException ("Invalid revocation mode.");
				}

			bool unknown = partial;
			// from the root down to the end-entity
			for (int i = elements.Count - 1; i >= 0; i--)
				{
				bool check = true;

				switch (ChainPolicy.RevocationFlag)
					{
					case X509RevocationFlag.EndCertificateOnly:
						check = (i == 0);
						break;
					case X509RevocationFlag.EntireChain:
						check = true;
						break;
					case X509RevocationFlag.ExcludeRoot:
						// default
						check = (i != (elements.Count - 1));
						// anyway, who's gonna sign that the root is invalid ?
						break;
					}

				X509ChainElement element = elements[i];

				// we can't assume the revocation status if the certificate is bad (e.g. invalid signature)
				if (!unknown)
					unknown |= ((element.StatusFlags & X509ChainStatusFlags.NotSignatureValid) != 0);

				if (unknown)
					{
					// we can skip the revocation checks as we can't be sure of them anyway
					element.StatusFlags |= X509ChainStatusFlags.RevocationStatusUnknown;
					element.StatusFlags |= X509ChainStatusFlags.OfflineRevocation;
					}
				else if (check && !partial && !IsSelfIssued (element.BCCertificate))
					{
					// check for revocation (except for the trusted root and self-issued certs)
					element.StatusFlags |= CheckRevocation (element.BCCertificate, i + 1, online);
					// if revoked, then all others following in the chain are unknown...
					unknown |= ((element.StatusFlags & X509ChainStatusFlags.Revoked) != 0);
					}
				}
			}

		// This isn't how RFC3280 (section 6.3) deals with CRL, but then we don't (yet) support DP, deltas...
		private X509ChainStatusFlags CheckRevocation (BCX509Certificate2 certificate, int ca, bool online)
			{
			X509ChainStatusFlags result = X509ChainStatusFlags.RevocationStatusUnknown;
			X509ChainElement element = elements[ca];
			BCX509Certificate2 ca_cert = element.BCCertificate;

			// find the CRL from the "right" CA
			while (IsSelfIssued (ca_cert) && (ca < elements.Count - 1))
				{
				// try with this self-issued
				result = CheckRevocation (certificate, ca_cert, online);
				if (result != X509ChainStatusFlags.RevocationStatusUnknown)
					break;
				ca++;
				element = elements[ca];
				ca_cert = element.BCCertificate;
				}
			if (result == X509ChainStatusFlags.RevocationStatusUnknown)
				result = CheckRevocation (certificate, ca_cert, online);
			return result;
			}

		private X509ChainStatusFlags CheckRevocation (BCX509Certificate2 certificate, BCX509Certificate2 ca_cert, bool online)
			{
			// change this if/when we support OCSP
			KeyUsage kue = KeyUsage.GetInstance (ca_cert.CertificateStructure.TbsCertificate.Extensions.GetExtension (X509Extensions.KeyUsage));
			if (kue != null)
				{
				// ... verify CrlSign is set
				//KeyUsage success = new KeyUsage(KeyUsage.CrlSign);
				if ((kue.IntValue & KeyUsage.CrlSign) != KeyUsage.CrlSign)
					{
					// FIXME - we should try to find an alternative CA that has the CrlSign bit
					return X509ChainStatusFlags.RevocationStatusUnknown;
					}
				}

			X509Crl crl = FindCrl (ca_cert);

			if ((crl == null) && online)
				{
				// FIXME - download and install new CRL
				// then you get a second chance
				// crl = FindCrl (ca_cert, ref valid, ref out_of_date);

				// We need to get the subjectAltName and an URI from there (or use OCSP)	
				// X509KeyUsageExtension subjectAltName = (ca_cert.Extensions["2.5.29.17"] as X509KeyUsageExtension);
				}

#if !NETCF
			if (crl != null)
				{
				// validate the digital signature on the CRL using the CA public key
				// note #1: we can't use X509Crl.VerifySignature(X509Certificate) because it duplicates
				// checks and we loose the "why" of the failure
				// note #2: we do this before other tests as an invalid signature could be a hacked CRL
				// (so anything within can't be trusted)
				if (!crl.VerifySignature (ca_cert.GetPublicKey()))
					return X509ChainStatusFlags.RevocationStatusUnknown;

				X509Crl.X509CrlEntry entry = crl.GetCrlEntry (certificate);
				if (entry != null)
					{
					// We have an entry for this CRL that includes an unknown CRITICAL extension
					// See [X.509 7.3] NOTE 4
					if (!ProcessCrlEntryExtensions (entry))
						return X509ChainStatusFlags.Revoked;

					// FIXME - a little more is involved
					if (entry.RevocationDate <= ChainPolicy.VerificationTime)
						return X509ChainStatusFlags.Revoked;
					}

				// are we overdue for a CRL update ? if so we can't be sure of any certificate status
				if (crl.NextUpdate.Value < ChainPolicy.VerificationTime)
					return X509ChainStatusFlags.RevocationStatusUnknown | X509ChainStatusFlags.OfflineRevocation;

				// we have a CRL that includes an unknown CRITICAL extension
				// we put this check at the end so we do not "hide" any Revoked flags
				if (!ProcessCrlExtensions (crl))
					return X509ChainStatusFlags.RevocationStatusUnknown;
				}
			else
#endif
				return X509ChainStatusFlags.RevocationStatusUnknown;
#if !NETCF
			return X509ChainStatusFlags.NoError;
#endif
			}

		private static X509Crl CheckCrls (string subject, string ski, X509Store store)
			{
			if (store == null)
				return null;

#if !NETCF
			var crls = store.Crls;
			foreach (X509Crl crl in crls)
				{
				if (crl.IssuerName == subject && (ski.Length == 0 || ski == GetAuthorityKeyIdentifier (crl)))
					return crl;
				}
#endif
			return null; // No CRL found
			}

		private X509Crl FindCrl (BCX509Certificate2 caCertificate)
			{
#if !NETCF
			string subject = caCertificate.SubjectDN.Decode (X500DistinguishedNameFlags.None);
			string ski = GetSubjectKeyIdentifier (caCertificate);

			// consider that the LocalMachine directories could not exists... and cannot be created by the user
			X509Crl result = CheckCrls (subject, ski, LMCAStore.Store);
			if (result != null)
				return result;
			if (location == StoreLocation.CurrentUser)
				{
				result = CheckCrls (subject, ski, UserCAStore.Store);
				if (result != null)
					return result;
				}

			// consider that the LocalMachine directories could not exists... and cannot be created by the user
			result = CheckCrls (subject, ski, LMRootStore.Store);
			if (result != null)
				return result;
			if (location == StoreLocation.CurrentUser)
				{
				result = CheckCrls (subject, ski, UserRootStore.Store);
				if (result != null)
					return result;
				}
#endif
			return null;
			}

#if !NETCF
		private bool ProcessCrlExtensions (X509Crl crl)
			{
			foreach (X509Extension ext in crl.Extensions)
				{
				if (ext.Critical)
					{
					switch (ext.Oid)
						{
						case "2.5.29.20": // cRLNumber
						case "2.5.29.35": // authorityKeyIdentifier
							// we processed/know about this extension
							break;
						default:
							return false;
						}
					}
				}
			return true;
			}

		private bool ProcessCrlEntryExtensions (X509Crl.X509CrlEntry entry)
			{
			foreach (X509Extension ext in entry.Extensions)
				{
				if (ext.Critical)
					{
					switch (ext.Oid)
						{
						case "2.5.29.21": // cRLReason
							// we processed/know about this extension
							break;
						default:
							return false;
						}
					}
				}
			return true;
			}
#endif
		}
	}

#else
namespace System.Security.Cryptography.X509Certificates {
	public class X509Chain {
		public bool Build (X509Certificate2 cert)
		{
			return false;
		}
	}
}
#endif