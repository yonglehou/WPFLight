using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using System.Xml.Linq;
using WPFLight.Helpers;

namespace System.Windows.Markup {
	public sealed class XamlReader {
		XamlReader () { }

		#region Eigenschaften

		public XDocument Document { get; private set; }

		#endregion

        /// <summary>
        /// loads a Xaml-Stream and converts it to an object
        /// </summary>
        /// <param name="stream"></param>
        /// <returns></returns>
        public static object Load (Stream stream) {
            using (var reader = new StreamReader(stream)) {
                return Parse(reader.ReadToEnd());
            }
        }

		/// <summary>
		/// Parses an object from XAML
		/// </summary>
		/// <param name="xaml">Xaml.</param>
		public static object Parse (string xaml) {
			var xdoc = XDocument.Parse (xaml);
			var reader = new XamlReader ();
			reader.Document = xdoc;
			reader.ReadNamespaces ();
			return reader.ReadElement (xdoc.Root, null, null, null);
		}

		/// <summary>
		/// creates a new object by its typename
		/// </summary>
		/// <returns>The object.</returns>
		/// <param name="element">Element.</param>
		object CreateObject (XElement element) {
			if (element == null)
				throw new ArgumentNullException ();
				
			return Activator.CreateInstance ( 
				Type.GetType (this.GetTypeName (element), true));
		}

		/// <summary>
		/// gets the full typename
		/// </summary>
		/// <returns>The type name.</returns>
		/// <param name="element">Element.</param>
		string GetTypeName (XElement element) {
			if (element == null)
				throw new ArgumentNullException ();

			return this.GetTypeName (element.Name.LocalName);
		}

		/// <summary>
		/// gets the full typename
		/// </summary>
		/// <returns>The type name.</returns>
		/// <param name="value">Value.</param>
		string GetTypeName ( string value ) {
			if (value == null)
				throw new ArgumentNullException ();

            // resolve namespace
			var tmp = value.IndexOf (":");
			if (tmp > -1) {
				var name = value.Substring (0, tmp);
                return value.Replace(
                    name + ":", namespaces[name].Replace("clr-namespace:", String.Empty).Trim() + ".");
			} else {
				// xmlns - Microsoft Xaml-Presentation Schema
				if (this.HasDefaultNamespace ()) {
					foreach (var type in Assembly.GetCallingAssembly ( ).GetTypes ( )) {
						if (type.Name == value) {
							value = type.FullName;
							break;
						}
					}
				}
				return value;
			}
		}

        /// <summary>
        /// gets the DependencyProperty of a type by its property-name
        /// </summary>
        /// <param name="propertyName"></param>
        /// <param name="targetType"></param>
        /// <returns></returns>
		DependencyProperty GetDependencyProperty ( string propertyName, Type targetType ) {
			if (String.IsNullOrEmpty (propertyName))
				throw new ArgumentException ();

			var value = default ( DependencyProperty );
			if (targetType != null ) {
				// gets the static DependencyProperty-field, 
				// like "static readonly DependencyProperty BackgroundProperty"
				var field = targetType.GetField (
					propertyName + "Property", 
					BindingFlags.FlattenHierarchy
						| BindingFlags.Public
						| BindingFlags.Static);

				value = ( DependencyProperty ) field.GetValue (null);
			}
			return value;
		}

        object CreateObject (XElement element, object parent) {
            if (element == null || parent == null )
                throw new ArgumentNullException();

            if (parent == null || parent is ICollection || parent.GetType() == typeof(Object) ) {
                return Activator.CreateInstance(
                    Type.GetType(this.GetTypeName(element), true));
            }else {
                var type = parent.GetType();
                var propInfo = type.GetProperty(element.Name.LocalName);
                var propType = propInfo.PropertyType;
                return Activator.CreateInstance(propType);
            }
        }
			
		/// <summary>
		/// reads a xml-element and also its children to return it as object
		/// </summary>
		/// <returns>The element.</returns>
		/// <param name="element">Element.</param>
		object ReadElement (
			XElement element, 
			Type targetType, 
			Type targetStyleType, 
			object parent ) {


			var isPropertyElement = element.Name.LocalName.Contains ( "." ) 
				&& element.Parent != null 
				&& element.Name.LocalName.StartsWith (
					element.Parent.Name.LocalName + ".");
					
			var item = default ( Object );
			if (targetType == null ) {
                if (parent != null && !( parent is ResourceDictionary))
                    item = CreateObject(element, parent);
                else
                    item = CreateObject(element);
            } else
                item = Activator.CreateInstance(targetType);

            if (item is System.Windows.Media.LinearGradientBrush) {
                Console.WriteLine(item);
            }

			// TODO SingleValue-Support like <Setter.Value>Red</Setter.Value>
			/*
			if (element.Name.LocalName == "Setter.Value") {
				if ( !String.IsNullOrEmpty ( element.Value ) ) {
					if (element.Value.StartsWith ("<") && element.Value.EndsWith ("<")) {

					}
				}
				return ReadValue (element.Value, typeof(Object));
			}
			*/
				

			if (item != null) {
				var itemType = item.GetType ();
                var contentPropertyInfo = default ( PropertyInfo );

                // check target-item for ContentProperty-Attrbitue,
                // which describes the property which is set by default
                // like <Style><Setter ... /></Style> -- Parent of Setter is the Style.Setters-Property
                var contentPropertyAttribute = ( ContentPropertyAttribute ) 
                    Attribute.GetCustomAttribute(  
                        itemType, 
                        typeof ( ContentPropertyAttribute ) );

                if ( contentPropertyAttribute != null
                        && !String.IsNullOrEmpty ( contentPropertyAttribute.Name ) ) {
                    contentPropertyInfo = itemType.GetProperty ( 
                        contentPropertyAttribute.Name );
                }

				if (element.HasAttributes) {
					// iterate attributes
					foreach (var attribute in element.Attributes ( )) {
						if ( !attribute.IsNamespaceDeclaration ) {
                            if (this.IsKeyAttribute(attribute)) {
								// save resource by key
								ResourceHelper.SetResourceKey (
										ReadValue ( attribute.Value,null ), item);
                            } else {
                                var name = attribute.Name.LocalName;
                                var value = attribute.Value;
                                var propInfo = itemType.GetProperty(name);
								if (propInfo != null  ) {
									if (propInfo.PropertyType == typeof ( DependencyProperty ) 
											&& targetStyleType != null) {
										// sets the DependecyProperty-Value
										propInfo.SetValue (
											item, 
											this.GetDependencyProperty (
												value, targetStyleType), 
											null);
									} else {
                                        if (propInfo.PropertyType != typeof(System.Object)
                                                && ( propInfo.PropertyType != typeof ( Type ) ) ) {
                                            propInfo.SetValue (
                                                item,
                                                Convert(
                                                    this.ReadValue(
                                                        attribute.Value, propInfo.PropertyType), 
                                                    propInfo.PropertyType),
                                                null );
                                        } else {
                                            // sets the converted value
                                            propInfo.SetValue(
                                                item,
                                                //Convert (
                                                    this.ReadValue(
                                                        attribute.Value,
                                                        propInfo.PropertyType),
                                                //propInfo.PropertyType ),
                                                null);
                                        }
									}
								}
                            }
						}
					}
				}

				var style = item as Style;
				if (style != null && style.TargetType != null)
					targetStyleType = style.TargetType;

				/* <Style targetType="{x:Type Button}">	<!-- targetType, Property of the Style-Object !>
				 * 	<Style.Setters>	 <!-- Child-Element, Property of its parent-element !>
				 * 		...
				 * 	</Style.Setters>
				 * </Style>
				 * */

				// iterate child-elements
				foreach (var childElement in element.Elements()) {
                    // check whether its a property of the parent
                    if (childElement.Name.LocalName.StartsWith(element.Name.LocalName + ".")) {
                        var name = childElement.Name.LocalName.Replace(
                                       element.Name.LocalName + ".", String.Empty);
							
                        var propInfo = itemType.GetProperty(name);
                        if (propInfo != null) {
                            propInfo.SetValue(
                                item,
                                this.ReadValue(
									this.ReadElement(
										childElement, 
										propInfo.PropertyType, 
										targetStyleType, 
										item),
                                    propInfo.PropertyType),
                                null);
                        }
                    } else {
						// child of a collection
						var childItem = this.ReadElement (
							childElement, null, targetStyleType, item);

						if (item is ResourceDictionary) {
							((ResourceDictionary)item).Add (
								ResourceHelper.GetResourceKey (childItem), childItem);
						} else {
                            if (item is IList) {
                                var parentCollection = item as IList;
                                if (parentCollection != null) {
                                    parentCollection.Add(childItem);
                                } else {
                                    // check for contentproperty-Attribute
                                    if (contentPropertyInfo != null) {
                                        var v = contentPropertyInfo.GetValue(item, null);
                                        var collection = v as IList;
                                        if (collection != null) {
                                            if (collection.GetType() == childItem.GetType()) {
                                                foreach (var c in ((IList)childItem)) {
                                                    collection.Add(c);
                                                }
                                            } else
                                                collection.Add(childItem);
                                        } else {
                                            var newItem = Activator.CreateInstance(contentPropertyInfo.PropertyType);
                                            contentPropertyInfo.SetValue(item, newItem, null);
                                            var list = newItem as IList;
                                            if (list != null)
                                                list.Add(childItem);
                                            else {
                                                // SOMETHING WRONG
                                            }
                                        }
                                    } else {
                                        // Maybe, something is wrong
                                        Console.WriteLine("");
                                    }
                                }
                            } else {

								if (isPropertyElement) {

									return childItem;

									/*
									var name = element.Name.LocalName.Replace(
										element.Parent.Name.LocalName + ".", String.Empty);

									var parentType = parent.GetType ();
									var parentProp = parentType.GetProperty (name);

									parentProp.SetValue (parent, childItem, null);

									Console.WriteLine("");
									*/
								}
								 

                                // Set Content

								//Console.WriteLine("");
                            }
						}

						// converts the value of setters and triggers to its DependencyProperty-Type
						ConvertPropertyValues (childItem);
                    }
				}
			}
			return item;
		}

        bool IsKeyAttribute (XAttribute attr) {

            // I need a way to find a key with its prefix like x:Key,
            // but at the moment I can only find attributes with the name "Key",

			// DIRTY
			return attr.Name.LocalName == "Key"; //attr.ToString ( ).StartsWith(GetXamlSchemaVariable() + ":Key");
        }
			
		void ConvertPropertyValues ( object value ) {
			if ( value != null ){
				var setter = value as Setter;
				if (setter != null) {
					if (setter.Value != null) {
						var valueType = setter.Value.GetType ();
						var dp = setter.Property;
						if (!dp.PropertyType.IsAssignableFrom (valueType)) {
							setter.Value = Convert (setter.Value, dp.PropertyType);
							return;
						}
					}
				}
				var trigger = value as Trigger;
				if (trigger != null) {
					var dp = trigger.Property;
					var valueType = trigger.Value.GetType ();
					if (!dp.PropertyType.IsAssignableFrom ( valueType )) {
                        trigger.Value = Convert(trigger.Value,dp.PropertyType);
						return;
					}
				}
			}
		}

        /// <summary>
        /// Converts a value to a specific type and uses its default converter
        /// or its attributed custom TypeConverter like [TypeConverter(typeof(BrushConverter))]...
        /// </summary>
        /// <param name="propertyType"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        object Convert (object value, Type propertyType) {
            if (value == null)
                return null;
								
#if ( WINDOWS_PHONE )
            var conv = TypeDescriptor.GetConverter(propertyType);
            if (conv != null)
                return conv.ConvertFrom(value);

            return System.Convert.ChangeType(
                value, propertyType, System.Globalization.CultureInfo.InvariantCulture);
#else 
			var conv = TypeDescriptor.GetConverter(propertyType);
            return conv.ConvertFrom(value);
#endif
        }

		object ReadValue (object value, Type targetType) {
			var prefix = GetXamlPrefix ();
            var stringValue = value as String;
            if (stringValue != null) {
                // check for Null-Value
                if (stringValue == "{" + prefix + ":Null}")
                    return null;

                // check for Type-Value
			    if (stringValue.StartsWith ("{" + prefix + ":Type")) {
				    var typeName = stringValue
					    .Replace ("{" + prefix + ":Type ", String.Empty)
					    .Replace ("}", String.Empty);

                    // create type by its name
				    return Type.GetType (
					    this.GetTypeName (typeName), 
                        true );
			    }
            }
			return value; //Convert(value, targetType);
		}

		/// <summary>
		/// reads the namespaces which are declared in the root-element (xmlns:c="...")
		/// </summary>
		void ReadNamespaces () {
			var dic = new Dictionary<string,string> ();
			foreach (var attribute in Document.Root.Attributes ( )) {
				if (attribute.IsNamespaceDeclaration) {
					dic [attribute.Name.LocalName] = attribute.Value;
				}
			}
			this.namespaces = dic;
		}

        /// <summary>
        /// gets the prefix which was defined in xaml
        /// </summary>
        /// <returns></returns>
		string GetXamlPrefix () {
			foreach (var ns in namespaces) {
				if (ns.Value == SCHEMA_XAML) {
					return ns.Key;
				}
			}
			return null;
		}

		bool HasDefaultNamespace () {
			return namespaces.ContainsValue (SCHEMA_PRESENTATION);
		}
			
		private Dictionary<string, string>  namespaces;

		const string SCHEMA_XAML 			= "http://schemas.microsoft.com/winfx/2006/xaml";
		const string SCHEMA_PRESENTATION	= "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
	}
}